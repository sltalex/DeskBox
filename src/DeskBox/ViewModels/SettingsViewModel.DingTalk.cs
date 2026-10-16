using DeskBox.Services;
using Microsoft.UI.Xaml;

namespace DeskBox.ViewModels;

public partial class SettingsViewModel
{
    private string _dingTalkStatusText = string.Empty;
    private bool _isDingTalkBusy;
    private bool _hasDingTalkIdentity;
    private string? _dingTalkOwner;

    public string DingTalkStatusText
    {
        get => _dingTalkStatusText;
        private set => SetProperty(ref _dingTalkStatusText, value);
    }

    public bool IsDingTalkBusy
    {
        get => _isDingTalkBusy;
        private set
        {
            if (SetProperty(ref _isDingTalkBusy, value))
            {
                OnPropertyChanged(nameof(DingTalkActionsEnabled));
                OnPropertyChanged(nameof(DingTalkLinkVisibility));
                OnPropertyChanged(nameof(DingTalkLinkedVisibility));
            }
        }
    }

    public bool DingTalkActionsEnabled => !_isDingTalkBusy && _authSession.IsAuthenticated;

    public Visibility DingTalkLinkVisibility => _hasDingTalkIdentity
        ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DingTalkLinkedVisibility => _hasDingTalkIdentity
        ? Visibility.Visible : Visibility.Collapsed;

    public async Task LinkDingTalkAsync()
    {
        if (!DingTalkActionsEnabled) return;
        IsDingTalkBusy = true;
        string? owner = _authSession.CurrentUser?.Id;
        DingTalkStatusText = _localizationService.T("Settings.DingTalk.Working");
        try
        {
            DingTalkDwsAuthStatus status = await _dingTalkDwsClient.GetAuthStatusAsync(_lifetimeCts.Token);
            if (!status.Authenticated || !status.TokenValid)
                status = await _dingTalkDwsClient.LoginAsync(_lifetimeCts.Token);
            if (!status.Authenticated)
                throw new InvalidOperationException("DWS authorization was not completed.");

            DingTalkProfile profile = await _dingTalkDwsClient.GetCurrentUserAsync(_lifetimeCts.Token);
            if (_authSession.CurrentUser?.Id != owner) return;
            DingTalkIdentityDto identity = await _dingTalkAccountService.LinkAsync(profile, _lifetimeCts.Token);
            if (_authSession.CurrentUser?.Id != owner) return;
            ApplyDingTalkIdentity(identity);
            DingTalkStatusText = _localizationService.T("Settings.DingTalk.Linked");
        }
        catch (AuthApiException ex)
        {
            DingTalkStatusText = _localizationService.T(ex.Code == "dingtalk_identity_taken"
                ? "Settings.DingTalk.Error.AlreadyLinked"
                : "Settings.DingTalk.Error.Server");
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (FileNotFoundException)
        {
            DingTalkStatusText = _localizationService.T("Settings.DingTalk.Error.MissingRuntime");
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            App.Log($"[DingTalk] Authorization failed: {ex.GetType().Name}");
            DingTalkStatusText = _localizationService.T("Settings.DingTalk.Error.Authorization");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            DingTalkStatusText = _localizationService.T("Settings.DingTalk.Error.Network");
        }
        finally
        {
            IsDingTalkBusy = false;
            OnPropertyChanged(nameof(DingTalkActionsEnabled));
        }
    }

    public async Task RefreshDingTalkAsync()
    {
        if (!DingTalkActionsEnabled) return;
        IsDingTalkBusy = true;
        string? owner = _authSession.CurrentUser?.Id;
        try
        {
            DingTalkProfile profile = await _dingTalkDwsClient.GetCurrentUserAsync(_lifetimeCts.Token);
            if (_authSession.CurrentUser?.Id != owner) return;
            DingTalkIdentityDto identity = await _dingTalkAccountService.LinkAsync(profile, _lifetimeCts.Token);
            if (_authSession.CurrentUser?.Id != owner) return;
            ApplyDingTalkIdentity(identity);
            DingTalkStatusText = _localizationService.T("Settings.DingTalk.Refreshed");
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            App.Log($"[DingTalk] Profile refresh failed: {ex.GetType().Name}");
            DingTalkStatusText = _localizationService.T("Settings.DingTalk.Error.Authorization");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            DingTalkStatusText = _localizationService.T("Settings.DingTalk.Error.Network");
        }
        catch (Exception)
        {
            DingTalkStatusText = _localizationService.T("Settings.DingTalk.Error.Server");
        }
        finally { IsDingTalkBusy = false; }
    }

    public async Task UnlinkDingTalkAsync()
    {
        if (!DingTalkActionsEnabled) return;
        IsDingTalkBusy = true;
        try
        {
            await _dingTalkAccountService.UnlinkAsync(_lifetimeCts.Token);
            _hasDingTalkIdentity = false;
            DingTalkStatusText = _localizationService.T("Settings.DingTalk.Unlinked");
            OnPropertyChanged(nameof(DingTalkLinkVisibility));
            OnPropertyChanged(nameof(DingTalkLinkedVisibility));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or AuthApiException)
        {
            DingTalkStatusText = _localizationService.T("Settings.DingTalk.Error.Server");
        }
        finally { IsDingTalkBusy = false; }
    }

    private async Task RestoreDingTalkIdentityAsync()
    {
        if (!_authSession.IsAuthenticated) return;
        string? owner = _authSession.CurrentUser?.Id;
        if (_dingTalkOwner != owner) ResetDingTalkPresentation();
        _dingTalkOwner = owner;
        try
        {
            DingTalkIdentityDto? identity = await _dingTalkAccountService.GetAsync(_lifetimeCts.Token);
            if (_authSession.CurrentUser?.Id != owner || _isDisposed) return;
            if (identity is not null) ApplyDingTalkIdentity(identity);
            else ResetDingTalkPresentation();
        }
        catch (Exception ex)
        {
            App.Log($"[DingTalk] Linked identity restore failed: {ex.GetType().Name}");
        }
    }

    private void ApplyDingTalkIdentity(DingTalkIdentityDto identity)
    {
        _hasDingTalkIdentity = true;
        OnPropertyChanged(nameof(DingTalkLinkVisibility));
        OnPropertyChanged(nameof(DingTalkLinkedVisibility));
    }

    private void ResetDingTalkPresentation()
    {
        _hasDingTalkIdentity = false;
        DingTalkStatusText = _localizationService.T("Settings.DingTalk.Description");
        OnPropertyChanged(nameof(DingTalkActionsEnabled));
        OnPropertyChanged(nameof(DingTalkLinkVisibility));
        OnPropertyChanged(nameof(DingTalkLinkedVisibility));
    }
}
