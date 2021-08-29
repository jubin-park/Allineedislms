using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Allineedislms
{
    class Program
    {
        private static readonly string OLD_LMS_LOGIN_URL = "https://ieilmsold.jbnu.ac.kr/login/index.php";
        private static readonly string OLD_LMS_REFERER_URL = "https://ieilmsold.jbnu.ac.kr/login.php";
        private static readonly string OLD_LMS_KLASS_LIST_URL = "https://ieilmsold.jbnu.ac.kr/local/ubion/user/index.php?lang=ko";
        private static readonly string OLD_LMS_CURRENT_KLASS_URL = "http://ieilmsold.jbnu.ac.kr/course/view.php?id=";
        private static readonly string OLD_LMS_LOGIN_REQUEST_DATA = "username={0}&password={1}";
        private static readonly string OLD_LMS_LOGIN_FAILED_MESSAGE = "?errorcode=";
        private static readonly string OLD_LMS_KLASS_LIST_HEAD = "<tbody class=\"my-course-lists\">";
        private static readonly string OLD_LMS_KLASS_LIST_TAIL = "</tbody>";

        private static readonly string NEW_LMS_LOGIN_URL = "https://ieilms.jbnu.ac.kr/login/authLoginProc.jsp";
        private static readonly string NEW_LMS_REFERER_URL = "https://ieilms.jbnu.ac.kr/";
        private static readonly string NEW_LMS_KLASS_LIST_URL = "https://ieilms.jbnu.ac.kr/mypage/group/myGroupList.jsp";
        private static readonly string NEW_LMS_CURRENT_KLASS_URL = "https://ieilms.jbnu.ac.kr/mypage/group/groupPage.jsp?group_id=";
        private static readonly string NEW_LMS_LOGIN_REQUEST_DATA = "login={0}&passwd={1}";
        private static readonly string NEW_LMS_LOGIN_SUCCESS_MESSAGE = "success_group";

        private static readonly Regex OLD_LMS_REGEX_KLASS = new Regex(@"<a href=""http://ieilmsold.jbnu.ac.kr/course/view.php\?id=(\d+)"" class=""coursefullname"">([\w+\s]*)\s\[(\d+)_(\d+)\]</a></div></td><td class=""text-center"">(\w+)</td><td class=""text-center"">(\d+)</td>");
        private static readonly Regex OLD_LMS_REGEX_USER_NAME = new Regex(@"<h4>(\w+)</h4>");
        private static readonly Regex NEW_LMS_REGEX_KLASS = new Regex(@"<a href=""javascript:selectGroup\((\d+),\'.*학기\s(.*)\((\d+)분반\)\'");
        private static readonly Regex NEW_LMS_REGEX_PROFESSOR_NAME = new Regex(@"<div style=""padding-bottom:2px;font-size:10px;color:gray;font-family:Malgun Gothic, gulim, dotum;"">(.*)</div>");

        private static readonly string OLD_LMS_TEMP_FILENAME = "oldlms.html";
        private static readonly string NEW_LMS_TEMP_FILENAME = "newlms.html";
        private static readonly string LMS_DATA_FILENAME = "lmsdata.json";

        private static readonly string[] TABLE_HEADER_NAMES = new string[7] { "index", "old-LMS", "new-LMS", "title", "class-#", "professor", "#-students" };
        private static readonly string VERTICAL_BAR = "| ";
        private static readonly string FORMAT_FIXED_WIDTH = "{{0,{0}}}";

        private static readonly string MESSAGE_SUCCESS = "SUCCESS !";
        private static readonly string MESSAGE_FAILED = "FAILED";
        private static readonly string MESSAGE_HELP =
@"r[un] [-i[gnore]] [-n[ologin]] [<1st-index>, <2nd-index>, ...]
	Launch webbrowser only for the indices marked
    If you do not enter any indices, they are all selected by default

    -ignore
        Run both versions of LMS, whether checked or not

    -nologin
        Runs without logging in

o[ld] [<1st-index>, <2nd-index>, ...]
	Check selected indices for running Old version of LMS with webbrowser
    If you do not enter any indices, they are all selected by default

n[ew] [<1st-index>, <2nd-index>, ...]
	Check selected indices for running New version of LMS with webbrowser
    If you do not enter any indices, they are all selected by default

b[oth] [<1st-index>, <2nd-index>, ...]
	Check selected indices for running Both versions of LMS with webbrowser
    If you do not enter any indices, they are all selected by default

c[lear] [<1st-index>, <2nd-index>, ...]
	Clear selected indices for running Both versions of LMS with webbrowser
    If you do not enter any indices, they are all selected by default

q[uit]
	Shutdown

h[elp]
	Print help document";

        private static CookieContainer s_newLmsCookie;
        private static CookieContainer s_oldLmsCookie;

        private static string s_userId;
        private static string s_userPassword;
        private static string s_userName = "me";
        private static List<LmsKlassInfo> s_LmsKlassInfos;
        private static EMenuStatus s_menuStatus = EMenuStatus.LOGIN;

        static void Main(string[] args)
        {
            if (args.Length == 2)
            {
                s_menuStatus = EMenuStatus.AUTO_LOGIN;
            }

            do
            {
                switch (s_menuStatus)
                {
                    case EMenuStatus.AUTO_LOGIN:
                        {
                            Console.WriteLine("* Enables automatic login" + Environment.NewLine);
                            s_userId = args[0];
                            s_userPassword = args[1];
                        }
                        s_menuStatus = EMenuStatus.AUTHENTICATION;
                        break;

                    case EMenuStatus.LOGIN:
                        {
                            Console.Write("JBNU id: ");
                            s_userId = inputNumberOnly();

                            Console.Write("password: ");
                            s_userPassword = inputPassword().Trim();
                        }
                        s_menuStatus = EMenuStatus.AUTHENTICATION;
                        break;

                    case EMenuStatus.AUTHENTICATION:
                        {
                            s_menuStatus = EMenuStatus.UPDATE;

                            Console.Write("- Sign in Old-LMS ... ");
                            if (canLoginOldLms())
                            {
                                Console.WriteLine(MESSAGE_SUCCESS);
                            }
                            else
                            {
                                Console.WriteLine(MESSAGE_FAILED);
                                s_menuStatus = EMenuStatus.LOGIN;
                            }

                            Console.Write("- Sign in New-LMS ... ");
                            if (canLoginNewLms())
                            {
                                Console.WriteLine(MESSAGE_SUCCESS);
                            }
                            else
                            {
                                Console.WriteLine(MESSAGE_FAILED);
                                s_menuStatus = EMenuStatus.LOGIN;
                            }
                        }
                        break;

                    case EMenuStatus.UPDATE:
                        {
                            s_menuStatus = EMenuStatus.MAIN;

                            bool bLoadable = true;
                            bool bError = false;

                            if (File.Exists(LMS_DATA_FILENAME))
                            {
                                Console.Write(string.Format("- Deserialize file \"{0}\" ... ", LMS_DATA_FILENAME));
                                try
                                {
                                    string jsonString = File.ReadAllText(LMS_DATA_FILENAME);
                                    s_LmsKlassInfos = JsonSerializer.Deserialize<List<LmsKlassInfo>>(jsonString);
                                    // valid check
                                    foreach (LmsKlassInfo klass in s_LmsKlassInfos)
                                    {
                                        if (klass.Name is null
                                            || klass.No == 0
                                            || klass.ProfessorName is null
                                            || klass.StudentCount == 0
                                            || klass.NewCode is null
                                            || klass.OldId == 0
                                            || klass.NewId == 0)
                                        {
                                            Console.WriteLine(MESSAGE_FAILED);
                                            bError = true;
                                            goto lb_error;
                                        }
                                    }
                                    Console.WriteLine(MESSAGE_SUCCESS);
                                }
                                catch
                                {
                                    bLoadable = false;
                                    Console.WriteLine(MESSAGE_FAILED);
                                }
                            }
                            else
                            {
                                bLoadable = false;
                            }

                            if (!bLoadable)
                            {
                                s_LmsKlassInfos = new List<LmsKlassInfo>();
                                Console.Write("- Get class list from LMS ... ");
                                if (tryUpdateLmsKlassInfos())
                                {
                                    Console.WriteLine(MESSAGE_SUCCESS);

                                    Console.WriteLine(string.Format("- Serialize file \"{0}\"", LMS_DATA_FILENAME));
                                    string jsonString = JsonSerializer.Serialize(s_LmsKlassInfos);
                                    File.WriteAllText(LMS_DATA_FILENAME, jsonString);
                                }
                                else
                                {
                                    Console.WriteLine(MESSAGE_FAILED);
                                    bError = true;
                                    goto lb_error;
                                }
                            }

                        lb_error:
                            if (bError)
                            {
                                s_menuStatus = EMenuStatus.QUIT;
                            }
                        }
                        break;

                    case EMenuStatus.MAIN:
                        {
                            Console.WriteLine();

                            int maxNameWidth = (int)Math.Ceiling((double)TABLE_HEADER_NAMES[3].Length / 2);
                            int maxProfessorNameWidth = (int)Math.Ceiling((double)TABLE_HEADER_NAMES[5].Length / 2);
                            foreach (LmsKlassInfo klass in s_LmsKlassInfos)
                            {
                                maxNameWidth = Math.Max(maxNameWidth, klass.Name.Length);
                                maxProfessorNameWidth = Math.Max(maxProfessorNameWidth, klass.ProfessorName.Length);
                            }
                            maxNameWidth <<= 1;
                            maxProfessorNameWidth <<= 1;

                            string border = string.Empty;
                            {
                                int padding = 0;

                                padding += TABLE_HEADER_NAMES[0].Length + VERTICAL_BAR.Length;
                                Console.Write(string.Format(FORMAT_FIXED_WIDTH, TABLE_HEADER_NAMES[0].Length) + VERTICAL_BAR, TABLE_HEADER_NAMES[0]);
                                border += new string('-', TABLE_HEADER_NAMES[0].Length) + '+';

                                padding += TABLE_HEADER_NAMES[1].Length + VERTICAL_BAR.Length;
                                Console.Write(string.Format(FORMAT_FIXED_WIDTH, TABLE_HEADER_NAMES[1].Length) + VERTICAL_BAR, TABLE_HEADER_NAMES[1]);
                                border += new string('-', TABLE_HEADER_NAMES[1].Length + VERTICAL_BAR.Length - 1) + '+';

                                padding += TABLE_HEADER_NAMES[2].Length + VERTICAL_BAR.Length;
                                Console.Write(string.Format(FORMAT_FIXED_WIDTH, TABLE_HEADER_NAMES[2].Length) + VERTICAL_BAR, TABLE_HEADER_NAMES[2]);
                                border += new string('-', TABLE_HEADER_NAMES[2].Length + VERTICAL_BAR.Length - 1) + '+';

                                padding += maxNameWidth;
                                WriteConsolePadded(TABLE_HEADER_NAMES[3], padding, ' ');
                                padding += VERTICAL_BAR.Length;
                                Console.Write(VERTICAL_BAR);
                                border += new string('-', maxNameWidth + VERTICAL_BAR.Length - 1) + '+';

                                padding += TABLE_HEADER_NAMES[4].Length + VERTICAL_BAR.Length;
                                Console.Write(string.Format(FORMAT_FIXED_WIDTH, TABLE_HEADER_NAMES[4].Length) + VERTICAL_BAR, TABLE_HEADER_NAMES[4]);
                                border += new string('-', TABLE_HEADER_NAMES[4].Length + VERTICAL_BAR.Length - 1) + '+';

                                padding += maxProfessorNameWidth;
                                WriteConsolePadded(TABLE_HEADER_NAMES[5], padding, ' ');
                                padding += VERTICAL_BAR.Length;
                                Console.Write(VERTICAL_BAR);
                                border += new string('-', maxProfessorNameWidth + VERTICAL_BAR.Length - 1) + '+';

                                padding += TABLE_HEADER_NAMES[6].Length + VERTICAL_BAR.Length;
                                Console.Write(string.Format(FORMAT_FIXED_WIDTH, TABLE_HEADER_NAMES[6].Length) + VERTICAL_BAR, TABLE_HEADER_NAMES[6]);
                                border += new string('-', TABLE_HEADER_NAMES[6].Length + VERTICAL_BAR.Length - 1) + '+';

                                Console.WriteLine();
                                Console.WriteLine(border);
                            }

                            for (int i = 0; i < s_LmsKlassInfos.Count; ++i)
                            {
                                int padding = 0;
                                LmsKlassInfo klass = s_LmsKlassInfos[i];

                                // index
                                padding += TABLE_HEADER_NAMES[0].Length + VERTICAL_BAR.Length;
                                Console.Write(string.Format(FORMAT_FIXED_WIDTH, TABLE_HEADER_NAMES[0].Length) + VERTICAL_BAR, i + 1);

                                // Old-LMS
                                padding += TABLE_HEADER_NAMES[1].Length + VERTICAL_BAR.Length;
                                Console.Write(string.Format(FORMAT_FIXED_WIDTH, TABLE_HEADER_NAMES[1].Length) + VERTICAL_BAR, klass.IsOldChecked ? 'v' : ' ');

                                // New-LMS
                                padding += TABLE_HEADER_NAMES[2].Length + VERTICAL_BAR.Length;
                                Console.Write(string.Format(FORMAT_FIXED_WIDTH, TABLE_HEADER_NAMES[2].Length) + VERTICAL_BAR, klass.IsNewChecked ? 'v' : ' ');

                                // title
                                padding += maxNameWidth;
                                WriteConsolePadded(klass.Name, padding, ' ');
                                padding += VERTICAL_BAR.Length;
                                Console.Write(VERTICAL_BAR);

                                // class-#
                                padding += TABLE_HEADER_NAMES[4].Length + VERTICAL_BAR.Length;
                                Console.Write(string.Format(FORMAT_FIXED_WIDTH, TABLE_HEADER_NAMES[4].Length) + VERTICAL_BAR, klass.No);

                                // professor
                                padding += maxProfessorNameWidth;
                                WriteConsolePadded(klass.ProfessorName, padding, ' ');
                                padding += VERTICAL_BAR.Length;
                                Console.Write(VERTICAL_BAR);

                                // #-students
                                padding += TABLE_HEADER_NAMES[6].Length + VERTICAL_BAR.Length;
                                Console.Write(string.Format(FORMAT_FIXED_WIDTH, TABLE_HEADER_NAMES[6].Length) + VERTICAL_BAR, klass.StudentCount);

                                // border
                                Console.WriteLine(Environment.NewLine + border);
                            }
                            Console.Write(Environment.NewLine + s_userName + "> ");

                            // input command parameter
                            string input = Console.ReadLine().Trim();
                            input = Regex.Replace(input, @"\s+", " ");
                            string[] parameters = input.Split(' ');

                            if (input.Length > 0 && parameters.Length > 0)
                            {
                                parameters[0] = parameters[0].ToLower();
                                switch (parameters[0])
                                {
                                    case "o":
                                    /* intentional fallthrough */
                                    case "old":
                                        setLmsFlags(parameters, true, false);
                                        break;

                                    case "n":
                                    /* intentional fallthrough */
                                    case "new":
                                        setLmsFlags(parameters, false, true);
                                        break;

                                    case "b":
                                    /* intentional fallthrough */
                                    case "both":
                                        setLmsFlags(parameters, true, true);
                                        break;

                                    case "c":
                                    /* intentional fallthrough */
                                    case "clear":
                                        setLmsFlags(parameters, false, false);
                                        break;

                                    case "r":
                                    /* intentional fallthrough */
                                    case "run":
                                        {
                                            bool bBrowserLoginRequired = true;
                                            bool bLmsMarkedCondition = true;

                                            List<int> indices = new List<int>();

                                            for (int i = 1; i < parameters.Length; ++i)
                                            {
                                                if (parameters[i] == "-n" || parameters[i] == "-nologin")
                                                {
                                                    bBrowserLoginRequired = false;
                                                }
                                                else if (parameters[i] == "-i" || parameters[i] == "-ignore")
                                                {
                                                    bLmsMarkedCondition = false;
                                                }
                                                else
                                                {
                                                    int index;
                                                    if (int.TryParse(parameters[i], out index) && --index >= 0 && index < s_LmsKlassInfos.Count)
                                                    {
                                                        indices.Add(index);
                                                    }
                                                }
                                            }

                                            if (indices.Count == 0)
                                            {
                                                for (int i = 0; i < s_LmsKlassInfos.Count; ++i)
                                                {
                                                    indices.Add(i);
                                                }
                                            }

                                            if (bBrowserLoginRequired)
                                            {
                                                File.WriteAllText(OLD_LMS_TEMP_FILENAME, string.Format(HTMLTemplate.IEILMS_LOGIN, OLD_LMS_LOGIN_URL, "username", "password", s_userId, s_userPassword));
                                                File.WriteAllText(NEW_LMS_TEMP_FILENAME, string.Format(HTMLTemplate.IEILMS_LOGIN, NEW_LMS_LOGIN_URL, "login", "passwd", s_userId, s_userPassword));

                                                Console.WriteLine("- Sign in Old-LMS with webbrowser");
                                                openUrl(OLD_LMS_TEMP_FILENAME);
                                                Thread.Sleep(800);
                                                Console.WriteLine("- Sign in New-LMS with webbrowser");
                                                openUrl(NEW_LMS_TEMP_FILENAME);

                                                Thread.Sleep(2500);
                                            }

                                            foreach (int index in indices)
                                            {
                                                LmsKlassInfo klass = s_LmsKlassInfos[index];
                                                if (!bLmsMarkedCondition || klass.IsOldChecked)
                                                {
                                                    Console.WriteLine(string.Format("- Navigate to Old-LMS {0} with webbrowser - {1}", klass.Name, OLD_LMS_CURRENT_KLASS_URL + klass.OldId));
                                                    openUrl(OLD_LMS_CURRENT_KLASS_URL + klass.OldId);
                                                    Thread.Sleep(100);
                                                }
                                                if (!bLmsMarkedCondition || klass.IsNewChecked)
                                                {
                                                    Console.WriteLine(string.Format("- Navigate to New-LMS {0} with webbrowser - {1}", klass.Name, NEW_LMS_CURRENT_KLASS_URL + klass.NewId));
                                                    openUrl(NEW_LMS_CURRENT_KLASS_URL + klass.NewId);
                                                    Thread.Sleep(100);
                                                }
                                            }

                                            if (bBrowserLoginRequired)
                                            {
                                                Thread.Sleep((int)2022.08);

                                                if (File.Exists(OLD_LMS_TEMP_FILENAME))
                                                {
                                                    try
                                                    {
                                                        File.Delete(OLD_LMS_TEMP_FILENAME);
                                                    }
                                                    catch { }
                                                }
                                                if (File.Exists(NEW_LMS_TEMP_FILENAME))
                                                {
                                                    try
                                                    {
                                                        File.Delete(NEW_LMS_TEMP_FILENAME);
                                                    }
                                                    catch { }
                                                }
                                            }
                                        }
                                        break;

                                    case "h":
                                    /* intentional fallthrough */
                                    case "help":
                                        Console.WriteLine(MESSAGE_HELP);
                                        break;

                                    case "q":
                                    /* intentional fallthrough */
                                    case "quit":
                                        s_menuStatus = EMenuStatus.QUIT;
                                        break;

                                    default:
                                        Console.WriteLine("- Unregistered command");
                                        break;
                                }
                            }
                        }
                        break;

                    default:
                        Debug.Assert(false, "invalid EMenuStatus");
                        break;
                }

            } while (s_menuStatus != EMenuStatus.QUIT);
        }

        // https://stackoverflow.com/questions/34894479/c-sharp-console-string-padding-with-unicode-characters
        private static void WriteConsolePadded(string value, int length, char padValue)
        {
            Console.Write(value);
            if (Console.CursorLeft < length)
            {
                Console.Write(new string(padValue, length - Console.CursorLeft));
            }
        }

        private static bool canLoginOldLms()
        {
            s_oldLmsCookie = new CookieContainer();

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(OLD_LMS_LOGIN_URL);
            request.Method = "POST";
            request.Referer = OLD_LMS_REFERER_URL;
            request.ContentType = "application/x-www-form-urlencoded";
            request.CookieContainer = s_oldLmsCookie;
            request.AllowAutoRedirect = false; // https://stackoverflow.com/questions/16720483/c-sharp-send-post-request-and-receive-303-statuscode

            StreamWriter sw = new StreamWriter(request.GetRequestStream());
            sw.Write(string.Format(OLD_LMS_LOGIN_REQUEST_DATA, s_userId, s_userPassword));
            sw.Close();

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();

            if (response.StatusCode == HttpStatusCode.RedirectMethod)
            {
                Stream stream = response.GetResponseStream();
                StreamReader reader = new StreamReader(stream, Encoding.UTF8);

                string result = reader.ReadToEnd();

                request.Abort();
                stream.Close();
                reader.Close();

                return !result.Contains(OLD_LMS_LOGIN_FAILED_MESSAGE);
            }

            request.Abort();

            return false;
        }

        private static bool canLoginNewLms()
        {
            s_newLmsCookie = new CookieContainer();
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(NEW_LMS_LOGIN_URL);

            request.Method = "POST";
            request.Referer = NEW_LMS_REFERER_URL;
            request.ContentType = "application/x-www-form-urlencoded";
            request.CookieContainer = s_newLmsCookie;

            StreamWriter sw = new StreamWriter(request.GetRequestStream());
            sw.Write(string.Format(NEW_LMS_LOGIN_REQUEST_DATA, s_userId, s_userPassword));
            sw.Close();

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Stream stream = response.GetResponseStream();
                StreamReader reader = new StreamReader(stream, Encoding.UTF8);

                string result = reader.ReadToEnd();

                request.Abort();
                stream.Close();
                reader.Close();

                return result.Equals(NEW_LMS_LOGIN_SUCCESS_MESSAGE);
            }

            request.Abort();
            return false;
        }

        private static bool tryUpdateLmsKlassInfos()
        {
            List<List<string>> rawFinalLmsKlassInfos = new List<List<string>>();

            { // Old-LMS
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(OLD_LMS_KLASS_LIST_URL);
                request.Method = "GET";
                Debug.Assert(s_oldLmsCookie != null);
                request.CookieContainer = s_oldLmsCookie;
                request.ContentType = "application/x-www-form-urlencoded";

                bool bError = false;

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    Stream stream = response.GetResponseStream();
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8, true);
                    string result = reader.ReadToEnd();

                    { // parse username
                        Match match = OLD_LMS_REGEX_USER_NAME.Match(result);
                        if (match.Success)
                        {
                            s_userName = match.Groups[1].Value;
                        }
                    }

                    int indexHead = result.IndexOf(OLD_LMS_KLASS_LIST_HEAD);
                    if (indexHead < 0)
                    {
                        bError = true;
                        goto lb_exit;
                    }
                    result = result.Substring(indexHead + OLD_LMS_KLASS_LIST_HEAD.Length);

                    int indexEnd = result.IndexOf(OLD_LMS_KLASS_LIST_TAIL);
                    if (indexEnd < 0)
                    {
                        bError = true;
                        goto lb_exit;
                    }
                    result = result.Substring(0, indexEnd);

                    MatchCollection matches = OLD_LMS_REGEX_KLASS.Matches(result);
                    Debug.Assert(matches.Count > 0);
                    if (matches.Count == 0)
                    {
                        bError = true;
                        goto lb_exit;
                    }

                    foreach (Match match in matches)
                    {
                        Debug.Assert(match.Success);
                        Debug.Assert(match.Groups.Count == 7);
                        if (!match.Success || match.Groups.Count != 7)
                        {
                            bError = true;
                            goto lb_exit;
                        }

                        rawFinalLmsKlassInfos.Add(new List<string>()
                        {
                            match.Groups[2].Value, // m_name
                            match.Groups[4].Value, // m_no
                            match.Groups[5].Value, // m_professorName
                            match.Groups[6].Value, // m_studentCount
                            match.Groups[3].Value, // m_newCode
                            match.Groups[1].Value, // m_oldId
                            // ... need to add m_newId later
                        });
                    }

                lb_exit:
                    request.Abort();
                    stream.Close();
                    reader.Close();

                    if (bError)
                    {
                        return false;
                    }
                }
            }

            
            { // New-LMS
                List<List<string>> rawNewLmsKlassInfos = new List<List<string>>();

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(NEW_LMS_KLASS_LIST_URL);
                request.Method = "GET";
                Debug.Assert(s_newLmsCookie != null);
                request.CookieContainer = s_newLmsCookie;
                request.ContentType = "application/x-www-form-urlencoded";

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    Stream stream = response.GetResponseStream();
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8, true);
                    string result = reader.ReadToEnd();

                    bool bError = false;

                    { // parse name, no, and id
                        MatchCollection matches = NEW_LMS_REGEX_KLASS.Matches(result);
                        Debug.Assert(matches.Count > 0);
                        if (matches.Count == 0)
                        {
                            bError = true;
                            goto lb_exit;
                        }

                        foreach (Match match in matches)
                        {
                            Debug.Assert(match.Success);
                            Debug.Assert(match.Groups.Count == 4);
                            if (!match.Success || match.Groups.Count != 4)
                            {
                                bError = true;
                                goto lb_exit;
                            }

                            rawNewLmsKlassInfos.Add(new List<string>()
                            {
                                match.Groups[2].Value, // m_name
                                match.Groups[3].Value, // m_no
                                null,                  // m_professorName
                                match.Groups[1].Value, // m_newId
                            });
                        }
                    }

                    { // parse professor name
                        MatchCollection matches = NEW_LMS_REGEX_PROFESSOR_NAME.Matches(result);
                        Debug.Assert(matches.Count > 0);

                        for (int i = 0; i < matches.Count; ++i)
                        {
                            Debug.Assert(matches[i].Success);
                            Debug.Assert(matches[i].Groups.Count == 2);
                            rawNewLmsKlassInfos[i][2] = matches[i].Groups[1].Value;
                            Debug.Assert(rawNewLmsKlassInfos[i][2] != null);
                        }
                    }
                
                lb_exit:
                    request.Abort();
                    stream.Close();
                    reader.Close();

                    if (bError)
                    {
                        return false;
                    }
                }

                // merge
                foreach (List<string> rawFinalKlass in rawFinalLmsKlassInfos)
                {
                    foreach (List<string> rawNewKlass in rawNewLmsKlassInfos)
                    {
                        if (rawNewKlass[0].Equals(rawFinalKlass[0])     // m_name
                            && rawNewKlass[1].Equals(rawFinalKlass[1])  // m_no
                            && rawNewKlass[2].Equals(rawFinalKlass[2])) // m_professorName
                        {
                            rawFinalKlass.Add(rawNewKlass[3]);          // m_newId
                            break;
                        }
                    }

                    Debug.Assert(rawFinalKlass.Count == 7);
                    if (rawFinalKlass.Count != 7)
                    {
                        return false;
                    }
                }
            }

            s_LmsKlassInfos.Clear();
            foreach (List<string> rawFinalKlass in rawFinalLmsKlassInfos)
            {
                s_LmsKlassInfos.Add(new LmsKlassInfo(rawFinalKlass));
            }

            return true;
        }

        private static void setLmsFlags(string[] parameters, bool bOld, bool bNew)
        {
            Debug.Assert(parameters != null);

            if (parameters.Length == 0)
            {
                return;
            }
            else if (parameters.Length == 1)
            {
                foreach (LmsKlassInfo klass in s_LmsKlassInfos)
                {
                    klass.IsOldChecked = bOld;
                    klass.IsNewChecked = bNew;
                }
            }
            else if (parameters.Length >= 2)
            {
                for (int i = 1; i < parameters.Length; ++i)
                {
                    int index;
                    if (int.TryParse(parameters[i], out index) && --index >= 0 && index < s_LmsKlassInfos.Count)
                    {
                        s_LmsKlassInfos[index].IsOldChecked = bOld;
                        s_LmsKlassInfos[index].IsNewChecked = bNew;
                    }
                }
            }

            string jsonString = JsonSerializer.Serialize(s_LmsKlassInfos);
            File.WriteAllText(LMS_DATA_FILENAME, jsonString);
        }

        private static string inputNumberOnly()
        {
            StringBuilder sb = new StringBuilder(16);

            while (true)
            {
                int x = Console.CursorLeft;
                ConsoleKeyInfo keyInfo = Console.ReadKey(true);

                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                else if (keyInfo.Key == ConsoleKey.Backspace && sb.Length > 0)
                {
                    sb.Length--;
                    Console.SetCursorPosition(x - 1, Console.CursorTop);
                    Console.Write(' ');
                    Console.SetCursorPosition(x - 1, Console.CursorTop);
                }
                else if (keyInfo.KeyChar >= '0' && keyInfo.KeyChar <= '9')
                {
                    sb.Append(keyInfo.KeyChar);
                    Console.Write(keyInfo.KeyChar);
                }
            }

            return sb.ToString();
        }

        // https://stackoverflow.com/questions/23433980/c-sharp-console-hide-the-input-from-console-window-while-typing
        private static string inputPassword()
        {
            StringBuilder sb = new StringBuilder(16);

            while (true)
            {
                int x = Console.CursorLeft;
                ConsoleKeyInfo keyInfo = Console.ReadKey(true);

                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                else if (keyInfo.Key == ConsoleKey.Backspace && sb.Length > 0)
                {
                    sb.Length--;
                    Console.SetCursorPosition(x - 1, Console.CursorTop);
                    Console.Write(' ');
                    Console.SetCursorPosition(x - 1, Console.CursorTop);
                }
                else if (keyInfo.KeyChar >= 0x20 && keyInfo.KeyChar <= 0x7f)
                {
                    sb.Append(keyInfo.KeyChar);
                    Console.Write('*');
                }
            }

            return sb.ToString();
        }

        private static void openUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }
    }
}
