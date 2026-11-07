namespace PickleballClub.Utilities
{
    public class Function
    {
        public static int _AccountId = 0;
        public static string _Username = string.Empty;
        public static string _FullName = string.Empty;
        public static string _Phone = string.Empty;
        public static string _Email = string.Empty;
        public static string _Avatar = string.Empty;
        public static string _SkillLevel = string.Empty;
        public static int _RoleId = 0;
        public static string _Message = string.Empty;
        public static string _MessageEmail = string.Empty;

        public static string TitleSlugGenerationAlias(string title)
        {
            return SlugGenerator.SlugGenerator.GenerateSlug(title);
        }

        public static bool IsLogin()
        {
            if (string.IsNullOrEmpty(Function._Username) || Function._AccountId <= 0)
            {
                return false;
            }
            return true;
        }

        public static bool IsAdmin()
        {
            return IsLogin() && _RoleId == 1;
        }
    }
}
