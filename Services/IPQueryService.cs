using Newtonsoft.Json;
using AutomaticAds.Utils;
using AutomaticAds.Models;

namespace AutomaticAds.Services;

public interface IIPQueryService
{
    Task<string> GetCountryCodeAsync(string ipAddress);
    Task<string> GetCountryNameAsync(string ipAddress);
}

public class IPQueryService : IIPQueryService
{
    private static readonly HttpClient _httpClient = new();

    public async Task<string> GetCountryCodeAsync(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return "TW"; // 預設給本地

        // 🎯【核心修正 1】：清洗 IP，去掉連接埠（例如把 114.34.29.54:27015 變成 114.34.29.54）
        if (ipAddress.Contains(':'))
        {
            ipAddress = ipAddress.Split(':')[0];
        }

        ipAddress = ipAddress.Trim();

        // 🎯【核心修正 2】：判定本機與虛擬 IP，直接回傳 TW，不再走外部網路查詢
        if (ipAddress == "127.0.0.1" || ipAddress == "localhost" || ipAddress == "::1" || ipAddress.StartsWith("192.168."))
        {
            return "TW";
        }

        try
        {
            string requestUri = $"{Constants.ApiUrls.CountryApiBase}{ipAddress}";
            HttpResponseMessage response = await _httpClient.GetAsync(requestUri).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogError($"Error getting country code. Status code: {response.StatusCode}");
                return "TW"; // 失敗時兜底給 TW，不給英文
            }

            string jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var countryResponse = JsonConvert.DeserializeObject<CountryApiResponse>(jsonResponse);

            if (countryResponse?.Country == null || string.IsNullOrWhiteSpace(countryResponse.Country))
            {
                LogError("Country field is null or empty in API response");
                return "TW"; // 查無結果時兜底給 TW
            }

            return countryResponse.Country;
        }
        catch (HttpRequestException ex)
        {
            LogError($"HttpRequestException in GetCountryCodeAsync: {ex.Message}");
            return "TW";
        }
        catch (JsonException ex)
        {
            LogError($"JsonException in GetCountryCodeAsync: {ex.Message}");
            return "TW";
        }
        catch (Exception ex)
        {
            LogError($"Exception in GetCountryCodeAsync: {ex.Message}");
            return "TW";
        }
    }

    public async Task<string> GetCountryNameAsync(string ipAddress)
    {
        string countryCode = await GetCountryCodeAsync(ipAddress);

        if (countryCode == Constants.ErrorMessages.CountryCodeError || countryCode == "TW")
            return "Taiwan";

        return CountryMapping.GetCountryName(countryCode);
    }

    private static void LogError(string message)
    {
        Console.WriteLine($"[AutomaticAds] {message}");
    }
}
