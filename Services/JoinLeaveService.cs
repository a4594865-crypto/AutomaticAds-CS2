using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

using AutomaticAds.Config;
using AutomaticAds.Managers;
using AutomaticAds.Models;
using AutomaticAds.Utils;

namespace AutomaticAds.Services;

public class JoinLeaveService
{
    private readonly BaseConfigs _config;
    private readonly MessageFormatter _messageFormatter;
    private readonly PlayerManager _playerManager;
    private readonly IIPQueryService _ipQueryService;
    private readonly HashSet<ulong> _processedJoins = new();
    private readonly HashSet<ulong> _processedLeaves = new();
    private readonly TimerManager _timerManager;

    public JoinLeaveService(BaseConfigs config, MessageFormatter messageFormatter, PlayerManager playerManager, IIPQueryService ipQueryService, TimerManager timerManager)
    {
        _config = config;
        _messageFormatter = messageFormatter;
        _playerManager = playerManager;
        _ipQueryService = ipQueryService;
        _timerManager = timerManager;
    }

    public async void HandlePlayerJoin(CCSPlayerController player)
    {
        if (!_config.EnableJoinLeaveMessages)
            return;

        if (!ShouldProcessJoinLeaveMessage(player))
            return;

        ulong steamId;
        string? playerName = null;
        string playerIp = string.Empty;

        try
        {
            steamId = player.SteamID;
            playerName = player.PlayerName;
            playerIp = player.GetPlayerIpAddress();
        }
        catch
        {
            return;
        }

        if (_processedJoins.Contains(steamId))
            return;

        _processedJoins.Add(steamId);

        var joinConfig = _config.JoinLeave.FirstOrDefault();
        if (joinConfig == null || !joinConfig.HasValidJoinMessage())
        {
            _processedJoins.Remove(steamId);
            return;
        }

        try
        {
            string formattedPrefix = _messageFormatter.FormatMessage(_config.ChatPrefix);
            
            // 🎯【核心大導正】：不管 UseMultiLang 有沒有開，進服一定強制去網絡查真正的地理位置 IP！
            PlayerInfo joiningPlayerInfo = await _playerManager.GetOrCreatePlayerInfoAsync(player, _ipQueryService);

            Server.NextFrame(() =>
            {
                try
                {
                    if (player.IsValidPlayer())
                    {
                        var validPlayers = _playerManager.GetValidPlayers();

                        foreach (var targetPlayer in validPlayers)
                        {
                            try
                            {
                                string joinMessage;

                                if (_config.UseMultiLang)
                                {
                                    var targetPlayerInfo = _playerManager.GetBasicPlayerInfo(targetPlayer);

                                    // 🎯 強制鎖定：CountryCode 必須等於真正的地理國家（如 TW、JP），絕對不准被遊戲語系（SC、en）覆蓋！
                                    var messagePlayerInfo = new Models.PlayerInfo
                                    {
                                        Name = joiningPlayerInfo.Name,
                                        SteamId = joiningPlayerInfo.SteamId,
                                        IpAddress = joiningPlayerInfo.IpAddress,
                                        CountryCode = joiningPlayerInfo.CountryCode, // 🗺️ 真正的地理位置代碼
                                        CountryName = joiningPlayerInfo.CountryName  
                                    };

                                    string targetLanguage = _messageFormatter.GetLanguageFromCountryCode(targetPlayerInfo.CountryCode);
                                    string message = joinConfig.GetJoinMessage(targetLanguage);
                                    if (string.IsNullOrWhiteSpace(message))
                                    {
                                        message = joinConfig.GetJoinMessage("en");
                                    }

                                    if (!string.IsNullOrWhiteSpace(message))
                                    {
                                        joinMessage = _messageFormatter.FormatMessageWithPlayerInfo(message, messagePlayerInfo, formattedPrefix);
                                        targetPlayer.PrintToChat(joinMessage);
                                    }
                                }
                                else
                                {
                                    joinMessage = _messageFormatter.FormatJoinLeaveMessage(joinConfig, joiningPlayerInfo, formattedPrefix, true);
                                    if (!string.IsNullOrWhiteSpace(joinMessage))
                                    {
                                        targetPlayer.PrintToChat(joinMessage);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[AutomaticAds] Error sending join message to player {targetPlayer.PlayerName ?? "Unknown"}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AutomaticAds] Error in HandlePlayerJoin NextFrame: {ex.Message}");
                }
                catch
                {
                    // 最終防線
                }
                finally
                {
                    _processedJoins.Remove(steamId);
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AutomaticAds] Error in HandlePlayerJoin: Player '{playerName ?? "Unknown"}' - {ex.Message}");
            _processedJoins.Remove(steamId);
        }
    }

    public void HandlePlayerLeave(CCSPlayerController player)
    {
        if (!_config.EnableJoinLeaveMessages)
            return;

        if (!ShouldProcessJoinLeaveMessage(player))
            return;

        ulong steamId;
        string? playerName = null;

        try
        {
            steamId = player.SteamID;
            playerName = player.PlayerName;
        }
        catch
        {
            return;
        }

        if (_processedLeaves.Contains(steamId))
            return;

        _processedLeaves.Add(steamId);

        var leaveConfig = _config.JoinLeave.FirstOrDefault();
        if (leaveConfig == null || !leaveConfig.HasValidLeaveMessage())
        {
            _processedLeaves.Remove(steamId);
            return;
        }

        try
        {
            string formattedPrefix = _messageFormatter.FormatMessage(_config.ChatPrefix);
            PlayerInfo leavingPlayerInfo = _playerManager.GetBasicPlayerInfo(player);

            Server.NextFrame(() =>
            {
                try
                {
                    var validPlayers = _playerManager.GetValidPlayers().Where(p => p.SteamID != steamId).ToList();
                    foreach (var targetPlayer in validPlayers)
                    {
                        try
                        {
                            string leaveMessage;

                            if (_config.UseMultiLang)
                            {
                                var targetPlayerInfo = _playerManager.GetBasicPlayerInfo(targetPlayer);
                                string targetLanguage = _messageFormatter.GetLanguageFromCountryCode(targetPlayerInfo.CountryCode);
                                string message = leaveConfig.GetLeaveMessage(targetLanguage);
                                if (string.IsNullOrWhiteSpace(message))
                                {
                                    message = leaveConfig.GetLeaveMessage("en");
                                }

                                if (!string.IsNullOrWhiteSpace(message))
                                {
                                    leaveMessage = _messageFormatter.FormatMessageWithPlayerInfo(message, leavingPlayerInfo, formattedPrefix);
                                    targetPlayer.PrintToChat(leaveMessage);
                                }
                            }
                            else
                            {
                                leaveMessage = _messageFormatter.FormatJoinLeaveMessage(leaveConfig, leavingPlayerInfo, formattedPrefix, false);
                                if (!string.IsNullOrWhiteSpace(leaveMessage))
                                {
                                    targetPlayer.PrintToChat(leaveMessage);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[AutomaticAds] Error sending leave message to player {targetPlayer.PlayerName ?? "Unknown"}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AutomaticAds] Error in HandlePlayerLeave NextFrame: {ex.Message}");
                }
                finally
                {
                    _processedLeaves.Remove(steamId);
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AutomaticAds] Error in HandlePlayerLeave: Player '{playerName ?? "Unknown"}' - {ex.Message}");
            _processedLeaves.Remove(steamId);
        }
    }

    public void OnPlayerDisconnect(CCSPlayerController player)
    {
        if (player?.IsValid == true)
        {
            try
            {
                ulong steamId = player.SteamID;
                _processedJoins.Remove(steamId);
                _processedLeaves.Remove(steamId);
            }
            catch
            {
                Console.WriteLine("[AutomaticAds] Error processing player disconnect: Player object is invalid.");
            }
        }
    }

    private bool ShouldProcessJoinLeaveMessage(CCSPlayerController player)
    {
        return _config.JoinLeave.Any() && player.IsValidPlayer();
    }
}
