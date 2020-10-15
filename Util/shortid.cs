// https://github.com/bolorundurowb/shortid

using System;
using System.Security.Cryptography;

namespace shortid
{
    public class ShortId
    {
        //private static RNGCryptoServiceProvider _seedRNG = new RNGCryptoServiceProvider();
        private static Random _random = new Random();

        private static object _lockObj = new object();
        private const string Capitals = "BCDFGHJKLMNPQRSTVWX";
        private const string Smalls = "bcdfghjlkmnpqrstvwxz";
        private const string Numbers = "0123456789";
        private const string Specials = "-_";
        private static string _pool = $"{Smalls}{Capitals}";

        /// <summary>
        /// Generates a random string of varying length
        /// </summary>
        /// <param name="useNumbers">Whether or not to include numbers</param>
        /// <param name="useSpecial">Whether or not special characters are included</param>
        /// <returns>A random string</returns>
        public static string Generate(bool useNumbers = false, bool useSpecial = true)
        {
            int length = _random.Next(7, 15);
            return Generate(useNumbers, useSpecial, length);
        }

        /// <summary>
        /// Generates a random string of a specified length with the option to add numbers and special characters
        /// </summary>
        /// <param name="useNumbers">Whether or not numbers are included in the string</param>
        /// <param name="useSpecial">Whether or not special characters are included</param>
        /// <param name="length">The length of the generated string</param>
        /// <returns>A random string</returns>
        public static string Generate(bool useNumbers, bool useSpecial, int length)
        {
            // lock (_lockObj)
            // {
            //     if (_random == null)
            //     {
            //         byte[] buffer = new byte[4];

            //         _seedRNG.GetBytes(buffer);

            //         _random = new Random(BitConverter.ToInt32(buffer, 0));
            //     }
            // }

            string pool = _pool;

            if (useNumbers)
            {
                pool = Numbers + pool;
            }
            if (useSpecial)
            {
                pool += Specials;
            }

            string output = string.Empty;

            lock (_lockObj)
            {
                for (int i = 0; i < length; i++)
                {
                    int charIndex = _random.Next(0, pool.Length);
                    output += pool[charIndex];
                }
            }

            return output;
        }

        /// <summary>
        /// Generates a random string of a specified length
        /// </summary>
        /// <param name="length">The length of the generated string</param>
        /// <returns>A random string</returns>
        public static string Generate(int length)
        {
            return Generate(false, true, length);
        }

        /// <summary>
        /// Changes the character set that id's are generated from
        /// </summary>
        /// <param name="characters">The new character set</param>
        /// <exception cref="InvalidOperationException">Thrown when the new character set is less than 20 characters</exception>
        public static void SetCharacters(string characters)
        {
            if (string.IsNullOrWhiteSpace(characters))
            {
                throw new ArgumentException("The replacement characters must not be null or empty.");
            }

            characters = characters
                .Replace(" ", "")
                .Replace("\t", "")
                .Replace("\n", "")
                .Replace("\r", "");

            if (characters.Length < 20)
            {
                throw new InvalidOperationException(
                    "The replacement characters must be at least 20 letters in length and without spaces.");
            }
            _pool = characters;
        }

        /// <summary>
        /// Sets the seed that the random generator works with.
        /// </summary>
        /// <param name="seed">The seed for the random number generator</param>
        public static void SetSeed(int seed)
        {
            _random = new Random(seed);
        }

        /// <summary>
        /// Resets the random number generator and character set
        /// </summary>
        public static void Reset()
        {
            _random = new Random();
            _pool = $"{Smalls}{Capitals}";
        }
    }
}