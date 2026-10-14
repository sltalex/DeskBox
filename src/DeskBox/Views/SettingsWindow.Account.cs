using Microsoft.UI.Xaml;

namespace DeskBox.Views;

public sealed partial class SettingsWindow
{
    private async void AccountSignInButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.SignInAccountAsync(AccountPasswordBox.Password);
        }
        finally
        {
            AccountPasswordBox.Password = string.Empty;
        }
    }

    private async void AccountRegisterButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.RegisterAccountAsync(AccountPasswordBox.Password);
        }
        finally
        {
            AccountPasswordBox.Password = string.Empty;
        }
    }

    private async void AccountSignOutButton_Click(object sender, RoutedEventArgs e)
    {
        AccountPasswordBox.Password = string.Empty;
        await ViewModel.SignOutAccountAsync();
    }
}
