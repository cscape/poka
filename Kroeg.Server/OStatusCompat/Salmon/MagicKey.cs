﻿using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;

namespace Kroeg.Server.Salmon
{
    public class MagicKey
    {
        private string[] _parts;
        private RSA _rsa;

        private static byte[] _decodeBase64Url(string data)
        {
            return Convert.FromBase64String(data.Replace('-', '+').Replace('_', '/'));
        }

        private static string _encodeBase64Url(byte[] data)
        {
            return Convert.ToBase64String(data, Base64FormattingOptions.None).Replace('+', '-').Replace('/', '_');
        }

        public MagicKey(string key)
        {
            if (key[0] == '<')
            {
                _rsa = RSA.Create();
                _rsa.FromXmlString(key);
            }
            else
            {
                _parts = key.Split('.');
                if (_parts[0] != "RSA") throw new Exception("Unknown magic key!");

                var rsaParams = new RSAParameters();
                rsaParams.Modulus = _decodeBase64Url(_parts[1]);
                rsaParams.Exponent = _decodeBase64Url(_parts[2]);

                _rsa = RSA.Create();
                _rsa.ImportParameters(rsaParams);
            }
        }

        public static MagicKey Generate()
        {
            var rsa = RSA.Create();
            rsa.KeySize = 2048;

            if (rsa.KeySize != 2048)
                rsa = new RSACng(2048);

            return new MagicKey(rsa.ToXmlString(true));
        }

        public static async Task<MagicKey> KeyForAuthor(ASObject obj)
        {
            var authorId = (string) obj["email"].FirstOrDefault()?.Primitive;
            if (authorId == null)
            {
                authorId = obj["name"].FirstOrDefault()?.Primitive + "@" + new Uri((string)obj["id"].First().Primitive).Host;
            }

            var domain = authorId.Split('@')[1];
            var hc = new HttpClient();
            var wf = JsonConvert.DeserializeObject<Controllers.WellKnownController.WebfingerResult>(await hc.GetStringAsync($"https://{domain}/.well-known/webfinger?resource=acct:{Uri.EscapeDataString(authorId)}"));
            var link = wf.links.FirstOrDefault(a => a.rel == "magic-public-key");
            if (link == null) return null;

            if (!link.href.StartsWith("data:")) return null; // does this happen?

            return new MagicKey(link.href.Split(new char[] { ',' }, 2)[1]);
        }

        public byte[] BuildSignedData(string data, string dataType, string encoding, string algorithm)
        {
            var sig = data + "." + _encodeBase64Url(Encoding.UTF8.GetBytes(dataType)) + "." + _encodeBase64Url(Encoding.UTF8.GetBytes(encoding)) + "." + _encodeBase64Url(Encoding.UTF8.GetBytes(algorithm));
            return Encoding.UTF8.GetBytes(sig);
        }

        public bool Verify(string signature, byte[] data)
        {
            return _rsa.VerifyData(data, _decodeBase64Url(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        public byte[] Sign(byte[] data)
        {
            return _rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        public string PrivateKey
        {
            get { return _rsa.ToXmlString(true); }
        }

        public string PublicKey
        {
            get
            {
                var parms = _rsa.ExportParameters(false);

                return string.Join(".", "RSA", _encodeBase64Url(parms.Modulus), _encodeBase64Url(parms.Exponent));
            }
        }
    }
}