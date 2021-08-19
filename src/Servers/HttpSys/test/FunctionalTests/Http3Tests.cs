// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Net.Http;
using System.Net.Quic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Testing;
using Microsoft.Net.Http.Headers;
using Xunit;

// Not tested here: Http.Sys supports sending an altsvc HTTP/2 frame if you enable the following reg key.
// reg add "HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\services\HTTP\Parameters" /v EnableAltSvc /t REG_DWORD /d 1 /f
// However, this only works with certificate bindings that specify a name. We test with the IP based bindings created by IIS Express.
// I don't know if the client supports the HTTP/2 altsvc frame.
namespace Microsoft.AspNetCore.Server.HttpSys
{
    [MsQuicSupported] // Required by HttpClient
    [Http3Supported]
    public class Http3Tests
    {
        [ConditionalFact]
        public async Task Http3_Direct()
        {
            using var server = Utilities.CreateDynamicHttpsServer(out var address, async httpContext =>
            {
                try
                {
                    Assert.True(httpContext.Request.IsHttps);
                    await httpContext.Response.WriteAsync(httpContext.Request.Protocol);
                }
                catch (Exception ex)
                {
                    await httpContext.Response.WriteAsync(ex.ToString());
                }
            });
            var handler = new HttpClientHandler();
            // Needed on CI, the IIS Express cert we use isn't trusted there.
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version30;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
            var response = await client.GetStringAsync(address);
            Assert.Equal("HTTP/3", response);
        }

        [ConditionalFact]
        public async Task Http3_AltSvcHeader_UpgradeFromHttp1()
        {
            var altsvc = "";
            using var server = Utilities.CreateDynamicHttpsServer(out var address, async httpContext =>
            {
                try
                {
                    Assert.True(httpContext.Request.IsHttps);
                    // Alt-Svc is not supported by Http.Sys, you need to add it yourself.
                    httpContext.Response.Headers.AltSvc = altsvc;
                    await httpContext.Response.WriteAsync(httpContext.Request.Protocol);
                }
                catch (Exception ex)
                {
                    await httpContext.Response.WriteAsync(ex.ToString());
                }
            });

            altsvc = $@"h3="":{new Uri(address).Port}""";
            var handler = new HttpClientHandler();
            // Needed on CI, the IIS Express cert we use isn't trusted there.
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var client = new HttpClient(handler);
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;

            // First request is HTTP/1.1, gets an alt-svc response
            var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            var response1 = await client.SendAsync(request);
            response1.EnsureSuccessStatusCode();
            Assert.Equal("HTTP/1.1", await response1.Content.ReadAsStringAsync());
            Assert.Equal(altsvc, response1.Headers.GetValues(HeaderNames.AltSvc).SingleOrDefault());

            // Second request is HTTP/3
            var response3 = await client.GetStringAsync(address);
            Assert.Equal("HTTP/3", response3);
        }

        [ConditionalFact]
        public async Task Http3_AltSvcHeader_UpgradeFromHttp2()
        {
            var altsvc = "";
            using var server = Utilities.CreateDynamicHttpsServer(out var address, async httpContext =>
            {
                try
                {
                    Assert.True(httpContext.Request.IsHttps);
                    // Alt-Svc is not supported by Http.Sys, you need to add it yourself.
                    httpContext.Response.Headers.AltSvc = altsvc;
                    await httpContext.Response.WriteAsync(httpContext.Request.Protocol);
                }
                catch (Exception ex)
                {
                    await httpContext.Response.WriteAsync(ex.ToString());
                }
            });

            altsvc = $@"h3="":{new Uri(address).Port}""";
            var handler = new HttpClientHandler();
            // Needed on CI, the IIS Express cert we use isn't trusted there.
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version20;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;

            // First request is HTTP/2, gets an alt-svc response
            var response2 = await client.GetAsync(address);
            response2.EnsureSuccessStatusCode();
            Assert.Equal(altsvc, response2.Headers.GetValues(HeaderNames.AltSvc).SingleOrDefault());
            Assert.Equal("HTTP/2", await response2.Content.ReadAsStringAsync());

            // Second request is HTTP/3
            var response3 = await client.GetStringAsync(address);
            Assert.Equal("HTTP/3", response3);
        }

        [ConditionalFact]
        public async Task Http3_ResponseTrailers()
        {
            using var server = Utilities.CreateDynamicHttpsServer(out var address, async httpContext =>
            {
                try
                {
                    Assert.True(httpContext.Request.IsHttps);
                    await httpContext.Response.WriteAsync(httpContext.Request.Protocol);
                    httpContext.Response.AppendTrailer("custom", "value");
                }
                catch (Exception ex)
                {
                    await httpContext.Response.WriteAsync(ex.ToString());
                }
            });
            var handler = new HttpClientHandler();
            // Needed on CI, the IIS Express cert we use isn't trusted there.
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version30;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
            var response = await client.GetAsync(address);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadAsStringAsync();
            Assert.Equal("HTTP/3", result);
            Assert.Equal("value", response.TrailingHeaders.GetValues("custom").SingleOrDefault());
        }

        [ConditionalFact]
        public async Task Http3_ResetBeforeHeaders()
        {
            using var server = Utilities.CreateDynamicHttpsServer(out var address, async httpContext =>
            {
                try
                {
                    Assert.True(httpContext.Request.IsHttps);
                    httpContext.Features.Get<IHttpResetFeature>().Reset(0x010b); // H3_REQUEST_REJECTED
                }
                catch (Exception ex)
                {
                    await httpContext.Response.WriteAsync(ex.ToString());
                }
            });
            var handler = new HttpClientHandler();
            // Needed on CI, the IIS Express cert we use isn't trusted there.
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version30;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(address));
            var qex = Assert.IsType<QuicStreamAbortedException>(ex.InnerException);
            Assert.Equal(0x010b, qex.ErrorCode);
        }

        [ConditionalFact]
        public async Task Http3_ResetAfterHeaders()
        {
            var headersReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var server = Utilities.CreateDynamicHttpsServer(out var address, async httpContext =>
            {
                try
                {
                    Assert.True(httpContext.Request.IsHttps);
                    await httpContext.Response.Body.FlushAsync();
                    await headersReceived.Task.DefaultTimeout();
                    httpContext.Features.Get<IHttpResetFeature>().Reset(0x010c); // H3_REQUEST_CANCELLED
                }
                catch (Exception ex)
                {
                    await httpContext.Response.WriteAsync(ex.ToString());
                }
            });
            var handler = new HttpClientHandler();
            // Needed on CI, the IIS Express cert we use isn't trusted there.
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version30;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
            var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead);
            headersReceived.SetResult();
            response.EnsureSuccessStatusCode();
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => response.Content.ReadAsStringAsync());
            var qex = Assert.IsType<QuicStreamAbortedException>(ex.InnerException?.InnerException?.InnerException);
            Assert.Equal(0x010c, qex.ErrorCode);
        }

        [ConditionalFact]
        public async Task Http3_AppExceptionAfterHeaders_InternalError()
        {
            var headersReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var server = Utilities.CreateDynamicHttpsServer(out var address, async httpContext =>
            {
                await httpContext.Response.Body.FlushAsync();
                await headersReceived.Task.DefaultTimeout();
                throw new Exception("App Exception");
            });
            var handler = new HttpClientHandler();
            // Needed on CI, the IIS Express cert we use isn't trusted there.
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version30;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

            var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead);
            headersReceived.SetResult();
            response.EnsureSuccessStatusCode();
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => response.Content.ReadAsStringAsync());
            var qex = Assert.IsType<QuicStreamAbortedException>(ex.InnerException?.InnerException?.InnerException);
            Assert.Equal(0x0102, qex.ErrorCode); // H3_INTERNAL_ERROR
        }

        [ConditionalFact]
        public async Task Http3_Abort_Cancel()
        {
            using var server = Utilities.CreateDynamicHttpsServer(out var address, httpContext =>
            {
                httpContext.Abort();
                return Task.CompletedTask;
            });
            var handler = new HttpClientHandler();
            // Needed on CI, the IIS Express cert we use isn't trusted there.
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version30;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(address));
            var qex = Assert.IsType<QuicStreamAbortedException>(ex.InnerException);
            Assert.Equal(0x010c, qex.ErrorCode); // H3_REQUEST_CANCELLED
        }
    }
}