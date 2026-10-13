import { ACTIONS, buildPrompt } from './prompts.js';

export class UserFacingError extends Error {}

export function resolveRoute(ctx, settings) {
  const selected = settings.provider ? { provider: settings.provider, model: settings.model } : ctx.get?.('agentDefaultModel')?.currentSelection();
  if (!selected?.provider || !selected?.model) throw new UserFacingError('请先在 DSH 中设置默认模型，或在划词设置中选择提供方和模型。');
  return { provider: selected.provider, model: selected.model };
}

export async function modelCatalog(ctx) {
  const groups = await Promise.all(ctx.llm.listProviders().map(async provider => {
    const id = typeof provider === 'string' ? provider : provider.id;
    let timer;
    try {
      const timeout = new Promise(resolve => { timer = setTimeout(() => resolve([]), 5000); timer.unref?.(); });
      const models = await Promise.race([ctx.llm.listModels(id), timeout]);
      return models.map(model => ({ provider: id, model: model.id, name: model.name }));
    } catch { return []; /* One unavailable adapter must not block the others. */ }
    finally { clearTimeout(timer); }
  }));
  return groups.flat();
}

/** One stateless LLM request; no agent loop, tools, workspace or chat session. */
export async function runAction(ctx, settings, request, signal, emit) {
  if (!ACTIONS.includes(request.action) || typeof request.text !== 'string' || !request.text.trim() || request.text.length > settings.maxChars || /\x00/.test(request.text)) throw new UserFacingError('文字为空、包含无效字符或超出长度限制');
  const route = resolveRoute(ctx, settings);
  const timeout = AbortSignal.timeout(settings.timeoutMs);
  const combined = AbortSignal.any([signal, timeout]);
  combined.throwIfAborted();
  const options = { ...route, ...buildPrompt(request.action, request.text, settings), tools: [], maxTokens: settings.maxTokens, signal: combined };
  // Effort ids belong to the adapter. Never assume every route accepts "off".
  if (typeof ctx.llm.resolveModelInfo === 'function') {
    const info = await ctx.llm.resolveModelInfo(route.provider, route.model, combined);
    const ids = info.reasoning?.efforts?.map(x => x.id) ?? [];
    const fast = ['off', 'none', 'minimal', 'low'].find(id => ids.includes(id));
    if (fast) options.reasoningEffort = fast;
  }
  emit({ type: 'started', route });
  let characters = 0, finished = false;
  const deliveredBlocks = new Set();
  for await (const chunk of ctx.llm.stream(options)) {
    combined.throwIfAborted();
    if (chunk.type === 'text-delta') {
      deliveredBlocks.add(chunk.index); characters += chunk.text.length;
      if (characters > 80000) throw new UserFacingError('输出超过安全上限');
      emit({ type: 'delta', text: chunk.text });
    } else if (chunk.type === 'block-end' && chunk.block?.type === 'text' && !deliveredBlocks.has(chunk.index)) {
      const text = chunk.block.text || ''; characters += text.length;
      if (characters > 80000) throw new UserFacingError('输出超过安全上限');
      emit({ type: 'delta', text });
    } else if (chunk.type === 'tool-call-delta') {
      throw new UserFacingError('模型试图调用工具；划词助手不执行工具。');
    } else if (chunk.type === 'finish') {
      finished = true;
      const reason = chunk.reason?.kind;
      if (reason === 'error' || reason === 'aborted') {
        const code = String(chunk.reason.failure?.code || 'MODEL_ERROR');
        // Provider errors can contain URLs/headers. Do not forward raw failures.
        throw new UserFacingError('模型请求未完成（' + code.replace(/[^A-Z0-9_-]/gi, '').slice(0, 80) + '），请检查 DSH 模型设置。');
      }
      if (!['stop', 'max-tokens'].includes(reason)) throw new UserFacingError('模型返回了不支持的结束状态');
      if (!characters) throw new UserFacingError('模型没有返回可见文字');
      emit({ type: 'done', truncated: reason === 'max-tokens' });
      break;
    }
  }
  if (!finished) throw new UserFacingError('模型连接中断，请重试');
}
