namespace DshSelectionAssistant {
  internal static class ControlStyles {
    internal const string Markup = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='ComboBox'>
    <Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Background' Value='{DynamicResource Soft}'/>
    <Setter Property='FontSize' Value='13'/><Setter Property='MinHeight' Value='34'/><Setter Property='Padding' Value='10,6'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'>
      <Grid>
        <ToggleButton Focusable='False' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
          <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='{DynamicResource Soft}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='6'/></ControlTemplate></ToggleButton.Template>
        </ToggleButton>
        <ContentPresenter Margin='10,6,28,6' VerticalAlignment='Center' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' IsHitTestVisible='False'/>
        <TextBlock Text='⌄' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,10,3' Foreground='{DynamicResource Muted}' IsHitTestVisible='False'/>
        <Popup Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False'>
          <Border Background='{DynamicResource Surface}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='6' Padding='4' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'>
            <ScrollViewer MaxHeight='260' CanContentScroll='True'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer>
          </Border>
        </Popup>
      </Grid>
      <ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ComboBoxItem'>
    <Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Padding' Value='10,7'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'>
      <Border Name='item' CornerRadius='4' Padding='{TemplateBinding Padding}' Background='Transparent'><ContentPresenter/></Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='item' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
        <Trigger Property='IsSelected' Value='True'><Setter Property='Foreground' Value='{DynamicResource Accent}'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='TabControl'>
    <Setter Property='Background' Value='{DynamicResource Surface}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TabControl'>
      <DockPanel><TabPanel IsItemsHost='True' DockPanel.Dock='Top' Margin='0,0,0,10'/><ContentPresenter Name='PART_SelectedContentHost' ContentSource='SelectedContent'/></DockPanel>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='TabItem'>
    <Setter Property='Foreground' Value='{DynamicResource Muted}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TabItem'>
      <Border Name='tab' Background='Transparent' CornerRadius='6' Padding='12,9' Margin='0,0,5,0'><ContentPresenter ContentSource='Header'/></Border>
      <ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='tab' Property='Background' Value='{DynamicResource Hover}'/><Setter Property='Foreground' Value='{DynamicResource Accent}'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='CheckBox'><Setter Property='Foreground' Value='{DynamicResource Ink}'/></Style>
  <Style TargetType='ToolTip'><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Background' Value='{DynamicResource Surface}'/></Style>
</ResourceDictionary>";
  }
}
