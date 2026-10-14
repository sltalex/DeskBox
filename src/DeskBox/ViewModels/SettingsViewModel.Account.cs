using DeskBox.Contracts;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using System.Text;

namespace DeskBox.ViewModels;

public partial class SettingsViewModel
{
    private readonly IAuthSession _authSession;
    private string _accountUsername = string.Empty;
    private string _accountStatusText = string.Empty;
    private bool _isAccountBusy;

    public string AccountUsername
    {
        get => _accountUsername;
        set => SetProperty(ref _accountUsername, value);
    }

    public string AccountStatusText
    {
        get => _accountStatusText;
        private set
        {
            if (SetProperty(ref _accountStatusText, value))
            {
                OnPropertyChanged(nameof(AccountStatusVisibility));
            }
        }
    }

    public bool IsAccountBusy
    {
        get => _isAccountBusy;
        private set
        {
            if (SetProperty(ref _isAccountBusy, value))
            {
                OnPropertyChanged(nameof(AccountActionsEnabled));
            }
        }
    }

    public bool AccountActionsEnabled => !IsAccountBusy;

    public Visibility AccountSignedOutVisibility => _authSession.IsAuthenticated
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility AccountSignedInVisibility => _authSession.IsAuthenticated
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility AccountStatusVisibility => string.IsNullOrEmpty(AccountStatusText)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string AccountSignedInText => _authSession.CurrentUser is { } user
        ? _localizationService.Format("Settings.Account.SignedInAs", user.Username)
        : string.Empty;

    public async Task<bool> RegisterAccountAsync(string password) =>
        await RunAccountAuthenticationAsync(
            (username, secret, token) => _authSession.RegisterAsync(username, secret, token),
            password);

    public async Task<bool> SignInAccountAsync(string password) =>
        await RunAccountAuthenticationAsync(
            (username, secret, token) => _authSession.SignInAsync(username, secret, token),
            password);

    public async Task SignOutAccountAsync()
    {
        if (IsAccountBusy)
        {
            return;
        }

        IsAccountBusy = true;
        AccountStatusText = string.Empty;
        try
        {
            await _authSession.SignOutAsync(_lifetimeCts.Token);
            AccountUsername = string.Empty;
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        finally
        {
            IsAccountBusy = false;
            RefreshAccountPresentation();
        }
    }

    private void InitializeAccount()
    {
        _authSession.SessionChanged += OnAccountSessionChanged;
        _ = RestoreAccountSessionAsync();
    }

    private async Task RestoreAccountSessionAsync()
    {
        try
        {
            await _authSession.RestoreAsync(_lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            App.Log($"[Auth] Session restore failed: {ex.GetType().Name}");
        }
        finally
        {
            RefreshAccountPresentation();
        }
    }

    private async Task<bool> RunAccountAuthenticationAsync(
        Func<string, string, CancellationToken, Task<AuthUser>> action,
        string password)
    {
        if (IsAccountBusy)
        {
            return false;
        }

        string username = AccountUsername.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, "\\A[A-Za-z0-9_]{3,32}\\z"))
        {
            AccountStatusText = _localizationService.T("Settings.Account.Error.InvalidUsername");
            return false;
        }
        int passwordLength = password.EnumerateRunes().Count();
        if (passwordLength is < 6 or > 128)
        {
            AccountStatusText = _localizationService.T("Settings.Account.Error.InvalidPassword");
            return false;
        }

        IsAccountBusy = true;
        AccountStatusText = _localizationService.T("Settings.Account.Working");
        try
        {
            await action(username, password, _lifetimeCts.Token);
            AccountStatusText = string.Empty;
            return true;
        }
        catch (AuthApiException ex)
        {
            AccountStatusText = _localizationService.T(ex.Code switch
            {
                "username_taken" => "Settings.Account.Error.UsernameTaken",
                "invalid_credentials" => "Settings.Account.Error.InvalidCredentials",
                "rate_limited" => "Settings.Account.Error.RateLimited",
                _ => "Settings.Account.Error.Server"
            });
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            AccountStatusText = _localizationService.T("Settings.Account.Error.Network");
        }
        catch (Exception ex)
        {
            App.Log($"[Auth] Account operation failed: {ex.GetType().Name}");
            AccountStatusText = _localizationService.T("Settings.Account.Error.Server");
        }
        finally
        {
            IsAccountBusy = false;
            RefreshAccountPresentation();
        }
        return false;
    }

    private void OnAccountSessionChanged()
    {
        RefreshAccountPresentation();
    }

    private void RefreshAccountPresentation()
    {
        OnPropertyChanged(nameof(AccountSignedOutVisibility));
        OnPropertyChanged(nameof(AccountSignedInVisibility));
        OnPropertyChanged(nameof(AccountSignedInText));
        OnPropertyChanged(nameof(AccountActionsEnabled));
    }
}
