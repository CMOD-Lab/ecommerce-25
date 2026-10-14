using EcommerceWebApi.Entities;
using EcommerceWebApi.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace EcommerceWebApi.Authentication
{
    public class JwtService : IJwtService
    {
        private readonly AppSettings _applicationSettings;
        private readonly UserService _userService;
        // cz-dotnet-1004: Injected IDistributedCache (Redis/ElastiCache) to persist refresh token
        // state across pod restarts and horizontal scaling on EKS, replacing in-memory token storage.
        private readonly IDistributedCache _distributedCache;
        private const string RefreshTokenCacheKeyPrefix = "jwt:refreshtoken:";

        public JwtService(
            IOptions<AppSettings> applicationSettings,
            UserService userService,
            IDistributedCache distributedCache)
        {
            _applicationSettings = applicationSettings.Value;
            _userService = userService;
            _distributedCache = distributedCache;
        }

        public async Task<bool> GenerateJWT(
            User user,
            bool isSecondFactorChecked,
            HttpContext context
        )
        {
            // Create JWT
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_applicationSettings.Secret);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(
                    new[]
                    {
                        new Claim("id", user.Id.ToString()),
                        new Claim("status", isSecondFactorChecked.ToString()),
                        new Claim("role", user.Role)
                    }
                ),
                Expires = DateTime.Now.AddMinutes(15),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha512Signature
                )
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var encrypterToken = tokenHandler.WriteToken(token);

            // Set JWT
            SetJWT(encrypterToken, context);

            // Create & set refresh token
            if (isSecondFactorChecked)
            {
                var refreshToken = GenerateRefreshToken();
                return await SetRefreshToken(refreshToken, user, context);
            }

            return true;
        }

        public void SetJWT(string encrypterToken, HttpContext context)
        {
            // Set JWT to cookie
            context.Response.Cookies.Append(
                "access_token",
                encrypterToken,
                new CookieOptions
                {
                    Expires = DateTime.Now.AddMinutes(15),
                    HttpOnly = true,
                    Secure = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.None
                }
            );
        }

        // cz-dotnet-1004: GenerateRefreshToken creates the token value; persistence is now handled
        // via Redis-backed IDistributedCache in SetRefreshToken, replacing in-memory token storage
        // so refresh token state survives pod restarts and horizontal scaling on EKS with ElastiCache.
        private static RefreshToken GenerateRefreshToken()
        {
            // Create refresh token
            var refreshToken = new RefreshToken()
            {
                Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                Expires = DateTime.Now.AddDays(7),
                Created = DateTime.Now
            };

            return refreshToken;
        }

        public async Task<bool> SetRefreshToken(
            RefreshToken refreshToken,
            User user,
            HttpContext context
        )
        {
            // Set refresh token to cookie
            context.Response.Cookies.Append(
                "refresh_token",
                refreshToken.Token,
                new CookieOptions
                {
                    Expires = refreshToken.Expires,
                    HttpOnly = true,
                    Secure = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.None
                }
            );

            // cz-dotnet-1004: Persist refresh token to Redis distributed cache (ElastiCache on EKS)
            // so token validity is shared across all pod replicas, replacing in-memory user token storage.
            string cacheKey = $"{RefreshTokenCacheKeyPrefix}{user.Id}";
            var tokenBytes = Encoding.UTF8.GetBytes(refreshToken.Token);
            await _distributedCache.SetAsync(
                cacheKey,
                tokenBytes,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpiration = refreshToken.Expires
                }
            );

            // Set refresh token to user
            return await _userService.UpdateUserTokenAsync(user, refreshToken);
        }

        public async Task<bool> RevokeToken(User user, HttpContext context)
        {
            // Revoke all tokens from cookies and user
            var result = await _userService.UpdateUserTokenAsync(user, null);
            if (!result)
            {
                return false;
            }

            // cz-dotnet-1004: Remove refresh token from Redis distributed cache on revocation
            // so token invalidation is propagated to all pod replicas immediately.
            string cacheKey = $"{RefreshTokenCacheKeyPrefix}{user.Id}";
            await _distributedCache.RemoveAsync(cacheKey);

            context.Response.Cookies.Delete("access_token");
            context.Response.Cookies.Delete("refresh_token");
            return true;
        }

        public User? GetUserFromJWT(HttpContext context)
        {
            // Get user from JWT
            var tokenString = context.Request.Cookies["access_token"];
            if (string.IsNullOrEmpty(tokenString))
            {
                return null;
            }

            try
            {
                // Read token
                var token = new JwtSecurityTokenHandler().ReadJwtToken(tokenString);
                return _userService.GetUserById(token.Claims.First(x => x.Type == "id").Value);
            }
            catch
            {
                return null;
            }
        }

        public string? GetTotpStatusFromJWT(HttpContext context)
        {
            // Get auth status from JWT
            var tokenString = context.Request.Cookies["access_token"];
            if (string.IsNullOrEmpty(tokenString))
            {
                return null;
            }

            try
            {
                // Read token
                var token = new JwtSecurityTokenHandler().ReadJwtToken(tokenString);
                return token.Claims.First(x => x.Type == "status").Value;
            }
            catch
            {
                return null;
            }
        }
    }
}
