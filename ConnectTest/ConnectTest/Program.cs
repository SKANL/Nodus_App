using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;

class Program {
    static async Task Main() {
        try {
            Console.WriteLine("Connecting to MongoDB Atlas endpoint with empty client certs...");
            using var client = new TcpClient("ac-hsphuwy-shard-00-00.tgno0xj.mongodb.net", 27017);
            using var sslStream = new SslStream(client.GetStream(), false, (sender, cert, chain, errs) => true);
            var options = new SslClientAuthenticationOptions {
                TargetHost = "ac-hsphuwy-shard-00-00.tgno0xj.mongodb.net",
                EnabledSslProtocols = SslProtocols.Tls12,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ClientCertificates = new X509CertificateCollection(),
                LocalCertificateSelectionCallback = (s,t,l,r,i) => null
            };
            await sslStream.AuthenticateAsClientAsync(options);
            Console.WriteLine("TLS handshake success!");
        } catch (Exception ex) {
            Console.WriteLine("TLS Error: " + ex.Message);
            if (ex.InnerException != null) Console.WriteLine("Inner: " + ex.InnerException.Message);
        }
    }
}