using System;
using System.Net;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Notifications.Trakt
{
    public interface ITraktProxy
    {
        string GetUserName(string accessToken);
        HttpRequest GetOAuthRequest(string callbackUrl);
        RefreshRequestResponse RefreshAuthToken(string refreshToken);
        void AddToCollection(TraktAddMoviePayload payload, string accessToken);
        ValidationFailure Test(TraktSettings settings);
    }

    public class TraktProxy : ITraktProxy
    {
        private const string URL = "https://api.trakt.tv";
        private const string OAuthUrl = "https://api.trakt.tv/oauth/authorize";
        private const string RedirectUri = "https://auth.servarr.com/v1/trakt/auth";
        private const string RenewUri = "https://auth.servarr.com/v1/trakt/renew";
        private const string ClientId = "64508a8bf370cee550dde4806469922fd7cd70afb2d5690e3ee7f75ae784b70e";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public TraktProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public void AddToCollection(TraktAddMoviePayload payload, string accessToken)
        {
            var request = BuildTraktRequest("sync/collection", HttpMethod.POST, accessToken);

            request.Headers.ContentType = "application/json";
            request.SetContent(payload.ToJson());

            try
            {
                _httpClient.Execute(request);
            }
            catch (HttpException ex)
            {
                _logger.Error(ex, "Unable to post payload {0}", payload);
                throw new TraktException("Unable to post payload", ex);
            }
        }

        public string GetUserName(string accessToken)
        {
            var request = BuildTraktRequest("users/settings", HttpMethod.GET, accessToken);

            try
            {
                var response = _httpClient.Get<UserSettingsResponse>(request);

                if (response != null && response.Resource != null)
                {
                    return response.Resource.User.Ids.Slug;
                }
            }
            catch (HttpException)
            {
                _logger.Warn($"Error refreshing trakt access token");
            }

            return null;
        }

        public HttpRequest GetOAuthRequest(string callbackUrl)
        {
            return new HttpRequestBuilder(OAuthUrl)
                            .AddQueryParam("client_id", ClientId)
                            .AddQueryParam("response_type", "code")
                            .AddQueryParam("redirect_uri", RedirectUri)
                            .AddQueryParam("state", callbackUrl)
                            .Build();
        }

        public RefreshRequestResponse RefreshAuthToken(string refreshToken)
        {
            var request = new HttpRequestBuilder(RenewUri)
                    .AddQueryParam("refresh_token", refreshToken)
                    .Build();

            return _httpClient.Get<RefreshRequestResponse>(request)?.Resource ?? null;
        }

        public ValidationFailure Test(TraktSettings settings)
        {
            try
            {
                GetUserName(settings.AccessToken);
                return null;
            }
            catch (HttpException ex)
            {
                if (ex.Response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _logger.Error(ex, "Access Token is invalid: " + ex.Message);
                    return new ValidationFailure("Token", "Access Token is invalid");
                }

                _logger.Error(ex, "Unable to send test message: " + ex.Message);
                return new ValidationFailure("Token", "Unable to send test message");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to send test message: " + ex.Message);
                return new ValidationFailure("", "Unable to send test message");
            }
        }

        private HttpRequest BuildTraktRequest(string resource, HttpMethod method, string accessToken)
        {
            var request = new HttpRequestBuilder(URL).Resource(resource).Build();

            request.Headers.Accept = HttpAccept.Json.Value;
            request.Method = method;

            request.Headers.Add("trakt-api-version", "2");
            request.Headers.Add("trakt-api-key", ClientId);

            if (accessToken.IsNotNullOrWhiteSpace())
            {
                request.Headers.Add("Authorization", "Bearer " + accessToken);
            }

            return request;
        }
    }
}
