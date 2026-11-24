namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Text.Json;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Signs SNS fixture messages with a self-signed test certificate, as Amazon SNS would.
    /// </summary>
    internal sealed class SnsTestSigner : IDisposable
    {
        internal const string TopicArn = "arn:aws:sns:us-west-2:123456789012:trelliskit-media-jobs";

        internal string CertificateUrl { get; }

        internal string CertificatePem { get; }

        private readonly RSA _Rsa;
        private readonly X509Certificate2 _Certificate;
        private int _Counter = 0;

        internal SnsTestSigner(string region, string? certificateHost = null)
        {
            _Rsa = RSA.Create(2048);
            CertificateRequest request = new CertificateRequest("CN=sns.test", _Rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            _Certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            CertificatePem = _Certificate.ExportCertificatePem();
            CertificateUrl = "https://" + (certificateHost ?? "sns." + region + ".amazonaws.com") + "/SimpleNotificationService-" + Guid.NewGuid().ToString("N") + ".pem";
        }

        internal string Sign(string type, string message, string? subscribeUrl, string version = "2", string topicArn = TopicArn)
        {
            SnsMessage sns = new SnsMessage
            {
                Type = type,
                MessageId = "msg-" + (++_Counter) + "-" + Guid.NewGuid().ToString("N"),
                TopicArn = topicArn,
                Message = message,
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                SignatureVersion = version,
                SigningCertURL = CertificateUrl,
                SubscribeURL = subscribeUrl,
                Token = type == "Notification" ? null : "token-abc"
            };
            byte[] data = Encoding.UTF8.GetBytes(SnsMessageVerifier.StringToSign(sns));
            sns.Signature = Convert.ToBase64String(_Rsa.SignData(data, version == "1" ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            Dictionary<string, string?> json = new Dictionary<string, string?>
            {
                { "Type", sns.Type }, { "MessageId", sns.MessageId }, { "TopicArn", sns.TopicArn }, { "Message", sns.Message }, { "Timestamp", sns.Timestamp },
                { "SignatureVersion", sns.SignatureVersion }, { "Signature", sns.Signature }, { "SigningCertURL", sns.SigningCertURL }
            };
            if (subscribeUrl != null) json["SubscribeURL"] = subscribeUrl;
            if (sns.Token != null) json["Token"] = sns.Token;
            return JsonSerializer.Serialize(json);
        }

        public void Dispose()
        {
            _Certificate.Dispose();
            _Rsa.Dispose();
        }
    }
}
