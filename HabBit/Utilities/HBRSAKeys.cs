using System.Numerics;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace HabBit.Utilities
{
    public class HBRSAKeys
    {
        public string Modulus { get; }
        public string Exponent { get; }
        public string PrivateExponent { get; }

        public HBRSAKeys(int rsaKeySize)
        {
            using (var rsa = new RSACryptoServiceProvider(rsaKeySize))
            {
                RSAParameters rsaKeys = rsa.ExportParameters(true);

                Modulus = ToHex(rsaKeys.Modulus);
                Exponent = ToHex(rsaKeys.Exponent);

                if (rsaKeys.D != null)
                    PrivateExponent = ToHex(rsaKeys.D);
            }
        }
        public HBRSAKeys(string exponent, string modulus)
        {
            Modulus = modulus;
            Exponent = exponent;
        }
        public HBRSAKeys(string exponent, string modulus, string privateExponent)
            : this(exponent, modulus)
        {
            PrivateExponent = privateExponent;
        }

        private string ToHex(byte[] data)
        {
            byte[] positiveLE = ToPositiveLE(data);
            var integer = new BigInteger(positiveLE);

            return integer.ToString("x");
        }
        private byte[] ToPositiveLE(byte[] bigEndianData)
        {
            var reversed = new List<byte>(bigEndianData);
            reversed.Reverse();

            if (reversed[reversed.Count - 1] > 127)
            {
                reversed.Add(0);
            }
            return reversed.ToArray();
        }
    }
}