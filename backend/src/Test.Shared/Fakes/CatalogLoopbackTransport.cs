namespace Test.Shared.Fakes
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Tests only: retain reviewed HTTPS source identity while routing actual HTTP messages to the local scripted server. Does not assert TLS or public DNS behavior.</summary>
    internal sealed class CatalogLoopbackTransport : DelegatingHandler
    {
        private readonly Uri _Base;
        internal CatalogLoopbackTransport(string baseUrl):base(new SocketsHttpHandler{AllowAutoRedirect=false,UseCookies=false}){_Base=new Uri(baseUrl);}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            request.RequestUri=new Uri(_Base,request.RequestUri!.PathAndQuery);
            return base.SendAsync(request,cancellationToken);
        }
    }
}
