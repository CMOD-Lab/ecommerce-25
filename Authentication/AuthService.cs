using EcommerceWebApi.Entities;
using EcommerceWebApi.Services;
using Microsoft.Extensions.Caching.Distributed;
using System.Security.Cryptography;
using System.Text.Json;

namespace EcommerceWebApi.Authentication
{
    public class AuthService : IAuthService
    {
        private readonly IUserService _userService;
        private readonly ITotpService _totpService;
        private readonly IJwtService _jwtService;
        // cz-dotnet-1004: Replaced in-memory CurrentUser property with Redis-backed IDistributedCache
        // so circuit/session state survives pod restarts and horizontal scaling on EKS with ElastiCache.
        private readonly IDistributedCache _distributedCache;
        private const string CurrentUserCacheKeyPrefix = "auth:currentuser:";

        public User CurrentUser
        {
            get
            {
                // Retrieve current user from Redis distributed cache instead of in-memory field
                var httpContextAccessor = _httpContextAccessor.HttpContext;
                if (httpContextAccessor == null) return null!;
                var sessionId = httpContextAccessor.Request.Cookies["session_id"] ?? httpContextAccessor.TraceIdentifier;
                var cacheKey = $"{CurrentUserCacheKeyPrefix}{sessionId}";
                var cachedBytes = _distributedCache.Get(cacheKey);
                if (cachedBytes == null) return null!;
                return JsonSerializer.Deserialize<User>(cachedBytes)!;
            }
            internal set
            {
                // Persist current user to Redis distributed cache instead of in-memory field
                var httpContextAccessor = _httpContextAccessor.HttpContext;
                if (httpContextAccessor == null) return;
                var sessionId = httpContextAccessor.Request.Cookies["session_id"] ?? httpContextAccessor.TraceIdentifier;
                var cacheKey = $"{CurrentUserCacheKeyPrefix}{sessionId}";
                if (value == null)
                {
                    _distributedCache.Remove(cacheKey);
                }
                else
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
                    _distributedCache.Set(
                        cacheKey,
                        bytes,
                        new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
                        }
                    );
                }
            }
        }

        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuthService(
            UserService userService,
            TotpService totpService,
            JwtService jwtService,
            IDistributedCache distributedCache,
            IHttpContextAccessor httpContextAccessor)
        {
            _userService = userService;
            _totpService = totpService;
            _jwtService = jwtService;
            _distributedCache = distributedCache;
            _httpContextAccessor = httpContextAccessor;
        }

        public enum AuthResult
        {
            UserNotExist,
            UserExisted,
            InvalidCredentials,
            NeedSecondFactorAuth,
            Success,
            Fail
        }

        // cz-dotnet-1004: CheckPassword remains a pure static helper (no circuit state);
        // the in-memory circuit state was in CurrentUser which is now Redis-backed above.
        private static bool CheckPassword(User user, string password)
        {
            using HMACSHA512? hmac = new(key: user.PasswordSalt);
            var compute = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
            return compute.SequenceEqual(user.PasswordHash);
        }

        public async Task<AuthResult> Login(string username, string password, HttpContext context)
        {
            var user = _userService.GetUserByName(username);
            if (user == null)
            {
                return AuthResult.UserNotExist;
            }

            if (!CheckPassword(user, password))
            {
                return AuthResult.InvalidCredentials;
            }

            if (user.IsTwoFactorAuthActivated)
            {
                return await _jwtService.GenerateJWT(user, isSecondFactorChecked: false, context)
                    ? AuthResult.NeedSecondFactorAuth
                    : AuthResult.Fail;
            }
            return await _jwtService.GenerateJWT(user, isSecondFactorChecked: true, context)
                ? AuthResult.Success
                : AuthResult.Fail;
        }

        public async Task<AuthResult> LoginSecondFactor(string totp, HttpContext context)
        {
            bool validated = _totpService.ValidateTotp(
                base32Secret: CurrentUser.SecretCode,
                totp: totp
            );
            if (!validated)
            {
                return AuthResult.InvalidCredentials;
            }

            var result = await _jwtService.RevokeToken(CurrentUser, context);
            if (!result)
            {
                return AuthResult.Fail;
            }

            return await _jwtService.GenerateJWT(CurrentUser, isSecondFactorChecked: true, context)
                ? AuthResult.Success
                : AuthResult.Fail;
        }

        public async Task<AuthResult> Register(string username, string password)
        {
            if (_userService.GetUserByName(username) != null)
            {
                return AuthResult.UserExisted;
            }

            var user = new User { Username = username };
            using (HMACSHA512? hmac = new())
            {
                user.PasswordSalt = hmac.Key;
                user.PasswordHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));

                user.Role = "User";
                user.IsTwoFactorAuthActivated = false;
            }
            ;

            var insertResult = await _userService.InsertUserAsync(user);
            return insertResult ? AuthResult.Success : AuthResult.Fail;
        }

        public async Task<AuthResult> RevokeToken(HttpContext context)
        {
            var revokeResult = await _jwtService.RevokeToken(CurrentUser, context);

            if (revokeResult)
            {
                return AuthResult.Success;
            }
            else
            {
                return AuthResult.Fail;
            }
        }

        public AuthResult GetQrCode(out string? uriString)
        {
            uriString = _totpService.GenerateQrCode(
                base32Secret: _totpService.GenerateBase32Secret(),
                username: CurrentUser.Username
            );

            return AuthResult.Success;
        }

        public async Task<AuthResult> ValidateQrCode(string qrCode, string totp)
        {
            string? secret = _totpService.GetSecretFromQrCode(qrCode);
            if (secret == null)
            {
                return AuthResult.InvalidCredentials;
            }

            bool validated = _totpService.ValidateTotp(base32Secret: secret, totp: totp);
            if (!validated)
            {
                return AuthResult.InvalidCredentials;
            }

            CurrentUser.SecretCode = secret;
            var updateResult = await _userService.UpdateUserAsync(CurrentUser);

            return updateResult ? AuthResult.Success : AuthResult.Fail;
        }
    }
}
