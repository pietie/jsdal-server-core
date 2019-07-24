using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace jsdal_server_core.Settings.ObjectModel
{
    public class ConnectionStringSecurity
    {
        //  const string KEY_FILEPATH = "./conn.key";

        private static string connectionPrivateKey = null;

        private Encryptor encryptor;

        private readonly IDataProtector _protector;

        public static ConnectionStringSecurity Instance { get;set; }
        public ConnectionStringSecurity(IDataProtectionProvider provider)
        {
            encryptor = new Encryptor(provider);
            // var serviceCollection = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            // serviceCollection.AddDataProtection();
            // var services = serviceCollection.BuildServiceProvider();

            // encryptor = Microsoft.Extensions
            //                     .DependencyInjection
            //                     .ActivatorUtilities
            //                     .CreateInstance<Settings.ObjectModel.ConnectionStringSecurity.Encryptor>(services);


            // setup the connection private key used for encryption/decryption if it does not already exist
            // // if (!File.Exists(KEY_FILEPATH))
            // // {
            // //     try
            // //     {// TODO: Replace console.log and console.error with Session Log
            // //         Console.WriteLine("Creating private key for Connections...");
            // //         Console.WriteLine("\t{0}", Path.GetFullPath(KEY_FILEPATH));

            // //         var key = StringCipher.Generate256BytesOfRandomEntropy();
            // //         var keyString = Convert.ToBase64String(key, Base64FormattingOptions.InsertLineBreaks);
            // //         File.WriteAllText(KEY_FILEPATH, keyString, Encoding.UTF8);

            // //         connectionPrivateKey = keyString;

            // //         Console.WriteLine("...private key created.");
            // //     }
            // //     catch (Exception e)
            // //     {
            // //         SessionLog.Exception(e);
            // //     }
            // // }
            // // else
            // // {
            // //     try
            // //     {
            // //         connectionPrivateKey = File.ReadAllText(KEY_FILEPATH, Encoding.UTF8);
            // //     }
            // //     catch (Exception e)
            // //     {
            // //         SessionLog.Exception(e);
            // //     }
            // // }
        }

        public string Encrypt(string text)
        {
            //connectionPrivateKey = "XYZ6C8DF278CD5931069B522E695D4F2";
            //return StringCipher.Encrypt(text, connectionPrivateKey);
            //return X.EncryptString(text, connectionPrivateKey);

            return encryptor.Encrypt(text);

        }

        public string Decrypt(string text)
        {
            //return StringCipher.Decrypt(text, connectionPrivateKey);
            //connectionPrivateKey = "XYZ6C8DF278CD5931069B522E695D4F2";
            //return X.DecryptString(text, connectionPrivateKey);
            if (encryptor.TryDecrypt(text, out string decrypted))
            {
                return decrypted;
            }

            return null;
        }

        public class Encryptor
        {
            private readonly IDataProtector _protector;

            public Encryptor(IDataProtectionProvider provider)
            {
                //var purpose = GetType().FullName;
                var purpose = "jsDALServer::ConnectionString";
                _protector = provider.CreateProtector(purpose);
            }

            // public string Encrypt<T>(T obj)
            // {
            //     var json = JsonConvert.SerializeObject(obj);

            //     return Encrypt(json);
            // }

            public string Encrypt(string plaintext)
            {
                return _protector.Protect(plaintext);
            }

            public bool TryDecrypt(string encryptedText, out string decryptedText)
            {
                try
                {
                    decryptedText = _protector.Unprotect(encryptedText);

                    return true;
                }
                catch (CryptographicException ce)
                {
                    decryptedText = null;
                    SessionLog.Exception(ce);
                    return false;
                }
            }
        }

        // public static class X
        // {
        //     public static string EncryptString(string text, string keyString)
        //     {
        //         var key = Encoding.UTF8.GetBytes(keyString);

        //         using (var aesAlg = Aes.Create())
        //         {
        //             using (var encryptor = aesAlg.CreateEncryptor(key, aesAlg.IV))
        //             {
        //                 using (var msEncrypt = new MemoryStream())
        //                 {
        //                     using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
        //                     using (var swEncrypt = new StreamWriter(csEncrypt))
        //                     {
        //                         swEncrypt.Write(text);
        //                     }

        //                     var iv = aesAlg.IV;

        //                     var decryptedContent = msEncrypt.ToArray();

        //                     var result = new byte[iv.Length + decryptedContent.Length];

        //                     Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
        //                     Buffer.BlockCopy(decryptedContent, 0, result, iv.Length, decryptedContent.Length);

        //                     return Convert.ToBase64String(result);
        //                 }
        //             }
        //         }
        //     }

        //     public static string DecryptString(string cipherText, string keyString)
        //     {
        //         var fullCipher = Convert.FromBase64String(cipherText);

        //         var iv = new byte[16];
        //         var cipher = new byte[16];

        //         Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
        //         Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, iv.Length);
        //         var key = Encoding.UTF8.GetBytes(keyString);

        //         using (var aesAlg = Aes.Create())
        //         {
        //             using (var decryptor = aesAlg.CreateDecryptor(key, iv))
        //             {
        //                 string result;
        //                 using (var msDecrypt = new MemoryStream(cipher))
        //                 {
        //                     using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
        //                     {
        //                         using (var srDecrypt = new StreamReader(csDecrypt))
        //                         {
        //                             result = srDecrypt.ReadToEnd();
        //                         }
        //                     }
        //                 }

        //                 return result;
        //             }
        //         }
        //     }
        // }


        // https://stackoverflow.com/a/10177020
        public static class StringCipher
        {
            // This constant is used to determine the keysize of the encryption algorithm in bits.
            // We divide this by 8 within the code below to get the equivalent number of bytes.
            //private const int Keysize = 256;
            private const int Keysize = 128;

            // This constant determines the number of iterations for the password bytes generation function.
            private const int DerivationIterations = 1000;

            public static string Encrypt(string plainText, string passPhrase)
            {
                // Salt and IV is randomly generated each time, but is preprended to encrypted cipher text
                // so that the same Salt and IV values can be used when decrypting.  
                var saltStringBytes = Generate256BitsOfRandomEntropy();
                var ivStringBytes = Generate128BitsOfRandomEntropy();
                var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
                using (var password = new Rfc2898DeriveBytes(passPhrase, saltStringBytes, DerivationIterations))
                {
                    var keyBytes = password.GetBytes(Keysize / 8);
                    using (var symmetricKey = new RijndaelManaged())
                    {
                        symmetricKey.BlockSize = 128;//256;
                        symmetricKey.Mode = CipherMode.CBC;
                        symmetricKey.Padding = PaddingMode.PKCS7;
                        using (var encryptor = symmetricKey.CreateEncryptor(keyBytes, ivStringBytes))
                        {
                            using (var memoryStream = new MemoryStream())
                            {
                                using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                                {
                                    cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length);
                                    cryptoStream.FlushFinalBlock();
                                    // Create the final bytes as a concatenation of the random salt bytes, the random iv bytes and the cipher bytes.
                                    var cipherTextBytes = saltStringBytes;
                                    cipherTextBytes = cipherTextBytes.Concat(ivStringBytes).ToArray();
                                    cipherTextBytes = cipherTextBytes.Concat(memoryStream.ToArray()).ToArray();
                                    memoryStream.Close();
                                    cryptoStream.Close();
                                    return Convert.ToBase64String(cipherTextBytes);
                                }
                            }
                        }
                    }
                }
            }

            public static string Decrypt(string cipherText, string passPhrase)
            {
                // Get the complete stream of bytes that represent:
                // [32 bytes of Salt] + [32 bytes of IV] + [n bytes of CipherText]
                var cipherTextBytesWithSaltAndIv = Convert.FromBase64String(cipherText);
                // Get the saltbytes by extracting the first 32 bytes from the supplied cipherText bytes.
                var saltStringBytes = cipherTextBytesWithSaltAndIv.Take(Keysize / 8).ToArray();
                // Get the IV bytes by extracting the next 32 bytes from the supplied cipherText bytes.
                var ivStringBytes = cipherTextBytesWithSaltAndIv.Skip(Keysize / 8).Take(Keysize / 8).ToArray();

                // Get the actual cipher text bytes by removing the first 64 bytes from the cipherText string.
                var cipherTextBytes = cipherTextBytesWithSaltAndIv.Skip((Keysize / 8) * 2).Take(cipherTextBytesWithSaltAndIv.Length - ((Keysize / 8) * 2)).ToArray();

                using (var password = new Rfc2898DeriveBytes(passPhrase, saltStringBytes, DerivationIterations))
                {
                    var keyBytes = password.GetBytes(Keysize / 8);
                    using (var symmetricKey = new RijndaelManaged())
                    {
                        symmetricKey.BlockSize = 128;//256;
                        symmetricKey.Mode = CipherMode.CBC;
                        symmetricKey.Padding = PaddingMode.PKCS7;
                        using (var decryptor = symmetricKey.CreateDecryptor(keyBytes, ivStringBytes))
                        {
                            using (var memoryStream = new MemoryStream(cipherTextBytes))
                            {
                                using (var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                                {
                                    var plainTextBytes = new byte[cipherTextBytes.Length];
                                    var decryptedByteCount = cryptoStream.Read(plainTextBytes, 0, plainTextBytes.Length);
                                    memoryStream.Close();
                                    cryptoStream.Close();
                                    return Encoding.UTF8.GetString(plainTextBytes, 0, decryptedByteCount);
                                }
                            }
                        }
                    }
                }
            }

            public static byte[] Generate128BitsOfRandomEntropy()
            {
                var randomBytes = new byte[16];
                using (var rngCsp = new RNGCryptoServiceProvider())
                {
                    // Fill the array with cryptographically secure random bytes.
                    rngCsp.GetBytes(randomBytes);
                }
                return randomBytes;
            }

            public static byte[] Generate256BitsOfRandomEntropy()
            {
                var randomBytes = new byte[32]; // 32 Bytes will give us 256 bits.
                using (var rngCsp = new RNGCryptoServiceProvider())
                {
                    // Fill the array with cryptographically secure random bytes.
                    rngCsp.GetBytes(randomBytes);
                }
                return randomBytes;
            }

            // PL: Created.
            public static byte[] Generate256BytesOfRandomEntropy()
            {
                var randomBytes = new byte[256];
                using (var rngCsp = new RNGCryptoServiceProvider())
                {
                    rngCsp.GetBytes(randomBytes);
                }
                return randomBytes;
            }
        }

    }



}