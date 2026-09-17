namespace DigitalSorter.AndroidTV;

public sealed class MainPage : ContentPage
{
    public MainPage()
    {
        BackgroundColor = Colors.Black;

        Content = new Grid
        {
            Children =
            {
                new Label
                {
                    Text = "DigitalSorter Android TV",
                    TextColor = Colors.White,
                    FontSize = 40,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            }
        };
    }
}
