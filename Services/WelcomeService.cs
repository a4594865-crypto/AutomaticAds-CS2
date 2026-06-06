using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API;
using AutomaticAds.Config;
using AutomaticAds.Config.Models;
using AutomaticAds.Managers;
using AutomaticAds.Utils;

namespace AutomaticAds.Services;

public class WelcomeService
{
    private readonly BaseConfigs _config;
    private readonly MessageFormatter _messageFormatter;
    private readonly TimerManager _timerManager;
    private readonly PlayerManager _playerManager;
    private readonly HashSet<ulong> _processedPlayers = new();

    public WelcomeService(BaseConfigs config, MessageFormatter messageFormatter, TimerManager timerManager, PlayerManager playerManager)
    {
        _config = config;
        _messageFormatter = messageFormatter;
        _timerManager = timerManager;
        _playerManager = playerManager;
    }

    public void SendWelcomeMessage(CCSPlayerController player)
    {
        if (!_config.EnableWelcomeMessage || player.IsBot)
            return;

        if (_processedPlayers.Contains(player.SteamID))
            return;

        _processedPlayers.Add(player.SteamID);

        foreach (var welcome in _config.Welcome)
        {
            ValidateWelcomeFlags(welcome);

            if (!welcome.HasValidMessage())
                continue;

            if (player.CanViewMessage(welcome.ViewFlag, welcome.ExcludeFlag))
            {
                _timerManager.AddTimer(_config.WelcomeDelay, () =>
                {
                    if (!player.IsValidPlayer())
                    {
                        return;
                    }

                    SendWelcomeToPlayer(player, welcome);
                });
            }
        }
    }

    public void OnPlayerDisconnect(CCSPlayerController player)
    {
        if (player?.IsValid == true)
        {
            _processedPlayers.Remove(player.SteamID);
        }
    }

    private void ValidateWelcomeFlags(WelcomeConfig welcome)
    {
        if (string.IsNullOrWhiteSpace(welcome.ViewFlag))
        {
            welcome.ViewFlag = Utils.Constants.AllPlayersFlag;
        }

        if (string.IsNullOrWhiteSpace(welcome.ExcludeFlag))
        {
            welcome.ExcludeFlag = string.Empty;
        }
    }

    private void SendWelcomeToPlayer(CCSPlayerController player, WelcomeConfig welcome)
    {
        try
        {
            string formattedPrefix = _messageFormatter.FormatMessage(_config.ChatPrefix);
            
            // 【架構優化】：改為異步兼容/確保國籍能正確更新的邏輯
            Server.NextFrame(async () =>
            {
                try
                {
                    if (!player.IsValidPlayer()) return;

                    string welcomeMessage;

                    if (_config.UseMultiLang)
                    {
                        Models.PlayerInfo playerInfo;

                        // 修正：如果需要更新國家資訊，強迫它去撈取最新的 IP 國籍（不再偷懶讀舊快取）
                        if (_playerManager.NeedsCountryUpdate(player.SteamID))
                        {
                            // 這裡我們修正為真正的非同步查詢，確保 VPN 切換能立刻抓到新 IP
                            playerInfo = await _playerManager.GetOrCreatePlayerInfoAsync(player, null!); 
                        }
                        else
                        {
                            playerInfo = _playerManager.GetBasicPlayerInfo(player);
                        }

                        welcomeMessage = _messageFormatter.FormatWelcomeMessage(welcome, playerInfo, formattedPrefix);
                    }
                    else
                    {
                        var basicPlayerInfo = _playerManager.GetBasicPlayerInfo(player);
                        basicPlayerInfo.CountryCode = Utils.Constants.ErrorMessages.Unknown;
                        basicPlayerInfo.CountryName = Utils.Constants.ErrorMessages.Unknown;

                        welcomeMessage = _messageFormatter.FormatWelcomeMessage(welcome, basicPlayerInfo, formattedPrefix);
                    }

                    if (!string.IsNullOrWhiteSpace(welcomeMessage))
                    {
                        _playerManager.SendMessageToPlayer(player, welcomeMessage);

                        if (!welcome.DisableSound && !string.IsNullOrWhiteSpace(_config.GlobalPlaySound))
                        {
                            _playerManager.PlaySoundToPlayer(player, _config.GlobalPlaySound);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AutomaticAds] Error inside SendWelcomeToPlayer Async: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AutomaticAds] Error sending welcome message to player {player.PlayerName ?? "Unknown"}: {ex.Message}");
        }
    }
}
