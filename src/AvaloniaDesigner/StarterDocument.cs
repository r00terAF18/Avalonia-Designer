namespace AvaloniaDesigner;

public static class StarterDocument
{
    public const string Source = """
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <UserControl.Styles>
            <Style Selector="Button.primaryAction">
              <Setter Property="Background" Value="#7864E8" />
              <Setter Property="Foreground" Value="White" />
              <Setter Property="CornerRadius" Value="8" />
              <Setter Property="Padding" Value="20,11" />
            </Style>
          </UserControl.Styles>
          <Canvas Background="{DynamicResource ThemeBackgroundBrush}">
            <Border Canvas.Left="40" Canvas.Top="40" Width="620" Height="390"
                    Background="{DynamicResource ThemeControlLowBrush}"
                    BorderBrush="#607864E8" BorderThickness="1" CornerRadius="16" Padding="32">
              <StackPanel Spacing="18">
                <TextBlock Text="YOUR NEXT GREAT IDEA" FontSize="11" FontWeight="SemiBold" Foreground="#A797FF" LetterSpacing="2" />
                <TextBlock Text="Make something beautiful." FontSize="28" FontWeight="SemiBold" />
                <TextBlock Text="A real Avalonia view. Ready to make your own."
                           Opacity="0.65" TextWrapping="Wrap" />
                <TextBox Watermark="Give your project a name" />
                <CheckBox Content="Build with a little imagination" IsChecked="True" />
                <StackPanel Orientation="Horizontal" Spacing="12">
                  <Button Content="Get started" Classes="primaryAction" />
                  <Button Content="Explore possibilities" Padding="20,11" />
                </StackPanel>
              </StackPanel>
            </Border>
            <TextBlock Canvas.Left="44" Canvas.Top="456" Text="Drag a control here to start designing" Opacity="0.5" FontSize="13" />
          </Canvas>
        </UserControl>
        """;

    public const string Blank = """
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Canvas Background="{DynamicResource ThemeBackgroundBrush}" />
        </UserControl>
        """;
}
