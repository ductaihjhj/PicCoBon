using System.Security.Cryptography;
using System.Text;

namespace PickleballClub.Utilities
{
    public class HashMD5
    {
        public static string GetMD5(string input)
        {
            using (var md5 = MD5.Create())
            {
                var data = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder();
                foreach (var c in data)
                {
                    sb.Append(c.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
