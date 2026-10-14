using OtpNet;
using System;
using System.Globalization;
using System.Text;
using System.Web;
using Microsoft.Extensions.Caching.Distributed;

namespace EcommerceWebApi.Authentication
{
    public class TotpService : ITotpService
    {
        // cz-dotnet-1004: Replaced in-memory static field with Redis-backed IDistributedCache
        // to ensure TOTP replay-prevention state survives pod restarts and horizontal scaling on EKS.
        private readonly IDistributedCache _distributedCache;
        private const string TotpWindowCacheKeyPrefix = "totp:timewindow:";

        private const string issuer = "EcommerceWebApi";

        public TotpService(IDistributedCache distributedCache)
        {
            _distributedCache = distributedCache;
        }

        public string GenerateBase32Secret()
        {
            var secret = KeyGeneration.GenerateRandomKey(20);
            return Base32Encoding.ToString(secret);
        }

        public string? GenerateQrCode(string? base32Secret, string? username)
        {
            // Validate inputs
            if (username == null || base32Secret == null)
            {
                return null;
            }

            try
            {
                // Generate URI for the QR Code
                var uriString = new OtpUri(OtpType.Totp, base32Secret, username, issuer).ToString();
                return uriString;
            }
            catch
            {
                return null;
            }
        }

        public string? GetSecretFromQrCode(string qrCode)
        {
            // Return null if validate failed
            if (ValidateAndExtractSecret(uriString: qrCode, out string? secret))
            {
                return secret;
            }
            else
            {
                return null;
            }
        }

        private static bool ValidateAndExtractSecret(string uriString, out string? secret)
        {
            // otpauth://totp/{issuer}:{username}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period=30
            secret = null!;

            if (
                Uri.TryCreate(uriString, UriKind.Absolute, out Uri? uri)
                && uri.Scheme == "otpauth"
                && uri.Host == "totp"
            )
            {
                var queryParameters = HttpUtility.ParseQueryString(uri.Query);
                if (queryParameters["secret"] != null && queryParameters["issuer"] == issuer)
                {
                    secret = queryParameters["secret"];
                    return true;
                }
            }

            return false;
        }

        public bool ValidateTotp(string? base32Secret, string totp)
        {
            try
            {
                var secret = Base32Encoding.ToBytes(base32Secret);

                // Get exact time for TOTP
                DateTime exactTime = GetNistTime();

                // Validate TOTP
                var correction = new TimeCorrection(exactTime);
                var totpValidator = new Totp(secret, timeCorrection: correction);
                bool verify = totpValidator.VerifyTotp(
                    totp,
                    out long timeWindowUsed,
                    VerificationWindow.RfcSpecifiedNetworkDelay
                );

                // cz-dotnet-1004: Check if TOTP has been used via Redis-backed distributed cache
                // instead of in-memory static field, enabling replay-prevention across all pods.
                string cacheKey = $"{TotpWindowCacheKeyPrefix}{base32Secret}";
                var cachedWindowBytes = _distributedCache.Get(cacheKey);
                long cachedTimeWindow = cachedWindowBytes != null
                    ? BitConverter.ToInt64(cachedWindowBytes, 0)
                    : 0L;

                if (cachedTimeWindow == timeWindowUsed)
                {
                    return false;
                }

                // cz-dotnet-1004: Persist used time window to Redis (ElastiCache) instead of
                // in-memory static field so all pod replicas share the same replay-prevention state.
                var timeWindowBytes = BitConverter.GetBytes(timeWindowUsed);
                _distributedCache.Set(
                    cacheKey,
                    timeWindowBytes,
                    new DistributedCacheEntryOptions
                    {
                        // TOTP window is 30 seconds; keep entry for 2 minutes to cover clock skew
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
                    }
                );

                return verify;
            }
            catch
            {
                throw;
            }
        }

        public static DateTime GetNistTime()
        {
            // Get UTC time from the response header of request to the configured time server URL
            string timeServerUrl = Environment.GetEnvironmentVariable("TIME_SERVER_URL") ?? "http://www.google.com";
            using var httpClient = new HttpClient();
            try
            {
                using var response = httpClient.GetAsync(timeServerUrl).Result;
                if (response.IsSuccessStatusCode && response.Headers.Date != null)
                {
                    return DateTime.ParseExact(
                        response.Headers.Date.Value.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'"),
                        "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
                        CultureInfo.InvariantCulture.DateTimeFormat,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal
                    );
                }
                else
                {
                    throw new Exception("Failed to get exact time for the TOTP");
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to get exact time for the TOTP", ex);
            }
        }
    }
}
