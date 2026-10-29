namespace VRCLearn.App.Views;

public partial class BusyDialog : DialogWindowBase
{
    public BusyDialog(string title, string message)
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
    }
}
