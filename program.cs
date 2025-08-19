using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Timers;
using System.Threading;

namespace Milestone2Scheduler
{

    // Custom Exceptions

    public class TimeConflictException : Exception
    {
        public TimeConflictException(string message) : base(message) { }
    }

    public class AppointmentNotFoundException : Exception
    {
        public AppointmentNotFoundException(string message) : base(message) { }
    }


    // Interfaces

    public interface ISchedulable
    {
        DateTime StartTime { get; }
        DateTime EndTime { get; }
        void Reschedule(DateTime newStart, DateTime newEnd);
    }

    public interface ICancelable
    {
        bool IsCanceled { get; }
        void Cancel(string reason);
    }


    // Domain Entities

    public class Client
    {
        public string Name { get; set; }
        public string Phone { get; set; }

        public Client() { }
        public Client(string name, string phone)
        {
            Name = name;
            Phone = phone;
        }
    }

    public class AppointmentEventArgs : EventArgs
    {
        public Appointment Appointment { get; private set; }
        public AppointmentEventArgs(Appointment appt) { Appointment = appt; }
    }

    // Base appointment (polymorphic)
    public class Appointment : ISchedulable, ICancelable
    {
        public Guid Id { get; private set; }
        public string Title { get; set; }
        public virtual string Kind { get { return "General"; } }
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }
        public Client Client { get; private set; }
        public bool IsCanceled { get; private set; }
        public string CancelReason { get; private set; }
        public bool ReminderSent { get; set; }

        public Appointment() { }

        public Appointment(string title, DateTime start, DateTime end, Client client)
        {
            if (end <= start) throw new TimeConflictException("End time must be after start time.");
            Id = Guid.NewGuid();
            Title = title;
            StartTime = start;
            EndTime = end;
            Client = client;
            IsCanceled = false;
            CancelReason = "";
            ReminderSent = false;
        }

        public void Reschedule(DateTime newStart, DateTime newEnd)
        {
            if (newEnd <= newStart) throw new TimeConflictException("End time must be after start time.");
            StartTime = newStart;
            EndTime = newEnd;
            ReminderSent = false;
        }

        public void Cancel(string reason)
        {
            IsCanceled = true;
            CancelReason = reason ?? "";
        }

        public override string ToString()
        {
            return string.Format("[{0}] {1} | {2:yyyy-MM-dd HH:mm} - {3:yyyy-MM-dd HH:mm} | {4} ({5}){6}",
                Kind, Title, StartTime, EndTime, Client != null ? Client.Name : "No Client", IsCanceled ? "Canceled" : "Active",
                IsCanceled && !string.IsNullOrWhiteSpace(CancelReason) ? " - " + CancelReason : "");
        }

        // CSV serialization (kept simple to avoid external packages; works on .NET Framework 4.8)

      
        public virtual string ToCsv()
        {
            return string.Join(",",
                Escape(Id.ToString()),
                Escape(Kind),
                Escape(Title),
                Escape(StartTime.ToString("yyyy-MM-dd HH:mm")),
                Escape(EndTime.ToString("yyyy-MM-dd HH:mm")),
                Escape(Client != null ? Client.Name : ""),
                Escape(Client != null ? Client.Phone : ""),
                Escape(IsCanceled.ToString()),
                Escape(CancelReason ?? ""),
                Escape(ReminderSent.ToString()),
                "", // DoctorName (only for DoctorAppointment)
                ""  // StylistName (only for SalonAppointment)
            );
        }

        protected static string Escape(string s)
        {
            if (s == null) return "";
            if (s.Contains(",") || s.Contains("\""))
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }

        protected static string Unescape(string s)
        {
            s = s.Trim();
            if (s.StartsWith("\"") && s.EndsWith("\""))
            {
                s = s.Substring(1, s.Length - 2).Replace("\"\"", "\"");
            }
            return s;
        }

        // Parse a CSV line into an Appointment or derived type
        public static Appointment FromCsv(string line)
        {
            // Basic CSV split that respects quotes
            List<string> cells = CsvSplit(line);
            if (cells.Count < 12) throw new FormatException("Invalid CSV row (expected 12 columns).");

            string idStr = Unescape(cells[0]);
            string kind = Unescape(cells[1]);
            string title = Unescape(cells[2]);
            string startStr = Unescape(cells[3]);
            string endStr = Unescape(cells[4]);
            string clientName = Unescape(cells[5]);
            string clientPhone = Unescape(cells[6]);
            string isCanceledStr = Unescape(cells[7]);
            string cancelReason = Unescape(cells[8]);
            string reminderSentStr = Unescape(cells[9]);
            string doctorName = Unescape(cells[10]);
            string stylistName = Unescape(cells[11]);

            DateTime start = DateTime.ParseExact(startStr, "yyyy-MM-dd HH:mm", null);
            DateTime end = DateTime.ParseExact(endStr, "yyyy-MM-dd HH:mm", null);

            Appointment appt;
            if (string.Equals(kind, "Doctor", StringComparison.OrdinalIgnoreCase))
            {
                appt = new DoctorAppointment(title, start, end, new Client(clientName, clientPhone), doctorName);
            }
            else if (string.Equals(kind, "Salon", StringComparison.OrdinalIgnoreCase))
            {
                appt = new SalonAppointment(title, start, end, new Client(clientName, clientPhone), stylistName);
            }
            else
            {
                appt = new Appointment(title, start, end, new Client(clientName, clientPhone));
            }

            Guid parsedId;
            if (Guid.TryParse(idStr, out parsedId))
                appt.Id = parsedId; // preserve original id

            bool isCanceled;
            if (bool.TryParse(isCanceledStr, out isCanceled) && isCanceled)
            {
                appt.Cancel(cancelReason);
            }

            bool reminderSent;
            if (bool.TryParse(reminderSentStr, out reminderSent))
                appt.ReminderSent = reminderSent;

            return appt;
        }

        // CSV splitter for quoted values
        private static List<string> CsvSplit(string line)
        {
            List<string> cells = new List<string>();
            StringBuilder sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                    {
                        sb.Append('\"'); // escaped quote
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    cells.Add(sb.ToString());
                    sb.Length = 0;
                }
                else
                {
                    sb.Append(c);
                }
            }
            cells.Add(sb.ToString());
            return cells;
        }
    }

    public class DoctorAppointment : Appointment
    {
        public string DoctorName { get; private set; }
        public override string Kind { get { return "Doctor"; } }

        public DoctorAppointment() { }

        public DoctorAppointment(string title, DateTime start, DateTime end, Client client, string doctorName)
            : base(title, start, end, client)
        {
            DoctorName = doctorName;
        }

        public override string ToCsv()
        {
            // Fill DoctorName column
            string baseCsv = base.ToCsv();
            // Replace the last two empty columns with doctorName and empty stylist
            string[] cols = SplitCsvPreservingQuotes(baseCsv);
            cols[10] = Escape(DoctorName ?? "");
            cols[11] = "";
            return string.Join(",", cols);
        }

        private static string[] SplitCsvPreservingQuotes(string line)
        {
            List<string> parts = new List<string>();
            StringBuilder sb = new StringBuilder();
            bool quotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '\"')
                {
                    if (quotes && i + 1 < line.Length && line[i + 1] == '\"')
                    {
                        sb.Append('\"'); i++;
                    }
                    else
                    {
                        quotes = !quotes;
                    }
                }
                else if (ch == ',' && !quotes)
                {
                    parts.Add(sb.ToString()); sb.Length = 0;
                }
                else sb.Append(ch);
            }
            parts.Add(sb.ToString());
            return parts.ToArray();
        }
    }

    public class SalonAppointment : Appointment
    {
        public string StylistName { get; private set; }
        public override string Kind { get { return "Salon"; } }

        public SalonAppointment() { }

        public SalonAppointment(string title, DateTime start, DateTime end, Client client, string stylistName)
            : base(title, start, end, client)
        {
            StylistName = stylistName;
        }

        public override string ToCsv()
        {
            string baseCsv = base.ToCsv();
            string[] cols = SplitCsvPreservingQuotes(baseCsv);
            cols[10] = ""; // DoctorName
            cols[11] = Escape(StylistName ?? "");
            return string.Join(",", cols);
        }

        private static string[] SplitCsvPreservingQuotes(string line)
        {
            List<string> parts = new List<string>();
            StringBuilder sb = new StringBuilder();
            bool quotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '\"')
                {
                    if (quotes && i + 1 < line.Length && line[i + 1] == '\"')
                    {
                        sb.Append('\"'); i++;
                    }
                    else
                    {
                        quotes = !quotes;
                    }
                }
                else if (ch == ',' && !quotes)
                {
                    parts.Add(sb.ToString()); sb.Length = 0;
                }
                else sb.Append(ch);
            }
            parts.Add(sb.ToString());
            return parts.ToArray();
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            if (s.Contains(",") || s.Contains("\""))
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }
    }


    // Schedule (thread-safe)

    public delegate void AppointmentNearHandler(object sender, AppointmentEventArgs e);

    public class Schedule
    {
        private readonly List<Appointment> _appointments = new List<Appointment>();
        private readonly object _lock = new object();

        // Raised by ReminderService (subscribers in Program)
        public event AppointmentNearHandler AppointmentNear;

        public void Add(Appointment appt)
        {
            if (appt == null) throw new ArgumentNullException("appt");

            lock (_lock)
            {
                // Overlap check with active appointments
                foreach (Appointment a in _appointments)
                {
                    if (!a.IsCanceled &&
                        appt.StartTime < a.EndTime &&
                        appt.EndTime > a.StartTime)
                    {
                        throw new TimeConflictException("Appointment overlaps with another active appointment.");
                    }
                }
                _appointments.Add(appt);
            }
        }

        public void Cancel(Guid id, string reason)
        {
            lock (_lock)
            {
                Appointment a = _appointments.FirstOrDefault(x => x.Id == id);
                if (a == null) throw new AppointmentNotFoundException("Appointment not found.");
                a.Cancel(reason);
            }
        }

        public void Reschedule(Guid id, DateTime newStart, DateTime newEnd)
        {
            lock (_lock)
            {
                Appointment target = _appointments.FirstOrDefault(x => x.Id == id);
                if (target == null) throw new AppointmentNotFoundException("Appointment not found.");

                // simulate removal to check overlap with others
                Appointment temp = target;
                DateTime oldStart = temp.StartTime; DateTime oldEnd = temp.EndTime;

                // Temporarily set times to validate against others
                temp.Reschedule(newStart, newEnd);

                foreach (Appointment a in _appointments)
                {
                    if (a.Id == temp.Id || a.IsCanceled) continue;
                    if (temp.StartTime < a.EndTime && temp.EndTime > a.StartTime)
                    {
                        // revert and throw
                        temp.Reschedule(oldStart, oldEnd);
                        throw new TimeConflictException("Reschedule would overlap with an existing appointment.");
                    }
                }
            }
        }

        public List<Appointment> All()
        {
            lock (_lock)
            {
                return new List<Appointment>(_appointments.OrderBy(a => a.StartTime));
            }
        }

        // Called by ReminderService
        internal void RaiseAppointmentNear(Appointment appt)
        {
            AppointmentNearHandler handler = AppointmentNear;
            if (handler != null) handler(this, new AppointmentEventArgs(appt));
        }

        // File I/O
        public void SaveCsv(string path)
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var sw = new StreamWriter(path, false, Encoding.UTF8))
                {
                    sw.WriteLine("Id,Kind,Title,StartTime,EndTime,ClientName,ClientPhone,IsCanceled,CancelReason,ReminderSent,DoctorName,StylistName");
                    foreach (Appointment a in _appointments)
                    {
                        sw.WriteLine(a.ToCsv());
                    }
                }
            }
        }

        public int LoadCsv(string path)
        {
            int imported = 0;
            if (!File.Exists(path)) return imported;

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i == 0 && lines[i].StartsWith("Id,Kind")) continue; // header
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    Appointment a = Appointment.FromCsv(line);
                    // if same Id exists, replace; else add with overlap check
                    lock (_lock)
                    {
                        int idx = _appointments.FindIndex(x => x.Id == a.Id);
                        if (idx >= 0) _appointments[idx] = a;
                        else
                        {
                            // Validate overlap only for active items
                            if (!a.IsCanceled)
                            {
                                foreach (Appointment ex in _appointments)
                                {
                                    if (!ex.IsCanceled &&
                                        a.StartTime < ex.EndTime &&
                                        a.EndTime > ex.StartTime)
                                        throw new TimeConflictException("Imported appointment overlaps.");
                                }
                            }
                            _appointments.Add(a);
                        }
                        imported++;
                    }
                }
                catch (Exception)
                {
                    // Skip bad rows; could log if needed by caller
                }
            }
            return imported;
        }
    }


    // Reminder Service (Timer-based, runs on background thread)

    public class ReminderService : IDisposable
    {
        private readonly Schedule _schedule;
        private readonly System.Timers.Timer _timer;
        private readonly object _scanLock = new object();

        public ReminderService(Schedule schedule)
        {
            _schedule = schedule;
            _timer = new System.Timers.Timer(30_000); // every 30s
            _timer.Elapsed += OnElapsed;
            _timer.AutoReset = true;
        }

        public void Start() { _timer.Start(); }
        public void Stop() { _timer.Stop(); }

        private void OnElapsed(object sender, ElapsedEventArgs e)
        {
            // prevent concurrent scans
            if (!Monitor.TryEnter(_scanLock)) return;
            try
            {
                List<Appointment> list = _schedule.All();
                DateTime now = DateTime.Now;

                foreach (Appointment a in list)
                {
                    if (a.IsCanceled) continue;
                    if (a.ReminderSent) continue;

                    double minutes = (a.StartTime - now).TotalMinutes;
                    if (minutes <= 10.0 && minutes > 0.0)
                    {
                        a.ReminderSent = true;
                        _schedule.RaiseAppointmentNear(a);
                    }
                }
            }
            finally
            {
                Monitor.Exit(_scanLock);
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }


    // Directory Monitor (FileSystemWatcher)

    public class FileImportedEventArgs : EventArgs
    {
        public string FilePath { get; private set; }
        public int ImportedCount { get; private set; }
        public FileImportedEventArgs(string filePath, int importedCount)
        {
            FilePath = filePath; ImportedCount = importedCount;
        }
    }
    public delegate void FileImportedHandler(object sender, FileImportedEventArgs e);

    public class DirectoryMonitor : IDisposable
    {
        private readonly string _watchDir;
        private readonly Schedule _schedule;
        private FileSystemWatcher _watcher;
        private bool _disposed;

        public event FileImportedHandler FileImported;

        public DirectoryMonitor(string watchDir, Schedule schedule)
        {
            _watchDir = watchDir;
            _schedule = schedule;
            Directory.CreateDirectory(_watchDir);

            _watcher = new FileSystemWatcher(_watchDir, "*.csv");
            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size;
            _watcher.Created += OnCreated;
            _watcher.EnableRaisingEvents = true;
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            // Wait until file is released by the writer
            for (int i = 0; i < 40; i++)
            {
                try
                {
                    using (FileStream fs = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        break;
                    }
                }
                catch (IOException)
                {
                    Thread.Sleep(100); // wait and retry
                }
            }

            int count = 0;
            try
            {
                count = _schedule.LoadCsv(e.FullPath);
                RaiseImported(e.FullPath, count);
            }
            catch (Exception)
            {
                // ignore file errors here
            }
        }

        private void RaiseImported(string path, int count)
        {
            FileImportedHandler handler = FileImported;
            if (handler != null) handler(this, new FileImportedEventArgs(path, count));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
        }
    }


    // Auth (very basic)

    public class AuthService
    {
        private readonly Dictionary<string, string> _users = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public AuthService()
        {
            // In a real app, hash & salt. Kept simple for milestone requirements.
            _users["admin"] = "admin123";
            _users["user"] = "password";
        }

        public bool Login(string username, string password)
        {
            string stored;
            if (_users.TryGetValue(username, out stored))
            {
                return string.Equals(stored, password);
            }
            return false;
        }
    }

    
    // Logger
    
    public static class Logger
    {
        private static readonly object _lock = new object();

        public static void Log(string path, string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                lock (_lock)
                {
                    File.AppendAllText(path,
                        string.Format("{0:yyyy-MM-dd HH:mm:ss} | {1}{2}", DateTime.Now, message, Environment.NewLine),
                        Encoding.UTF8);
                }
            }
            catch { /* swallow logging errors */ }
        }
    }

    
    // Program (UI)
    
    internal class Program
    {
        private static string BaseDir;
        private static string DataDir;
        private static string InboxDir;
        private static string LogsDir;

        private static string DataCsvPath;
        private static string ReminderLogPath;
        private static string ImportLogPath;

        private static Schedule schedule;
        private static ReminderService reminder;
        private static DirectoryMonitor dirMon;

        private static void Main(string[] args)
        {
            InitFolders();

            //  Login 
            var auth = new AuthService();
            Console.WriteLine("=== Michael & Themba Technologies — Appointment Scheduler ===");
            Console.Write("Username: ");
            string user = Console.ReadLine();
            Console.Write("Password: ");
            string pass = ReadPassword();

            if (!auth.Login(user, pass))
            {
                Console.WriteLine("Login failed.");
                return;
            }

            schedule = new Schedule();
            schedule.AppointmentNear += OnAppointmentNear;

            reminder = new ReminderService(schedule);
            reminder.Start();

            dirMon = new DirectoryMonitor(InboxDir, schedule);
            dirMon.FileImported += OnFileImported;

            Console.WriteLine("\nLogin successful.");
            Console.WriteLine("Data folder: " + DataDir);
            Console.WriteLine("Inbox (drop .csv to import): " + InboxDir);
            Console.WriteLine("Logs: " + LogsDir);

            RunMenu();

            // Cleanup
            reminder.Stop();
            reminder.Dispose();
            dirMon.Dispose();
        }

        private static void InitFolders()
        {
            BaseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProjectData");
            DataDir = Path.Combine(BaseDir, "data");
            InboxDir = Path.Combine(BaseDir, "inbox");
            LogsDir = Path.Combine(BaseDir, "logs");

            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(InboxDir);
            Directory.CreateDirectory(LogsDir);

            DataCsvPath = Path.Combine(DataDir, "appointments.csv");
            ReminderLogPath = Path.Combine(LogsDir, "reminders.log");
            ImportLogPath = Path.Combine(LogsDir, "imports.log");
        }

        private static void OnAppointmentNear(object sender, AppointmentEventArgs e)
        {
            Console.WriteLine();
            Console.WriteLine("Reminder: {0} with {1} at {2:HH:mm} ({3})",
                e.Appointment.Title,
                e.Appointment.Client != null ? e.Appointment.Client.Name : "Client",
                e.Appointment.StartTime,
                e.Appointment.Kind);

            Logger.Log(ReminderLogPath, "Reminder -> " + e.Appointment.ToString());
        }

        private static void OnFileImported(object sender, FileImportedEventArgs e)
        {
            Console.WriteLine();
            Console.WriteLine("Imported {0} appointment(s) from: {1}", e.ImportedCount, e.FilePath);
            Logger.Log(ImportLogPath, "Imported " + e.ImportedCount + " from " + e.FilePath);
        }

        private static void RunMenu()
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("======== MENU ========");
                Console.WriteLine("1) Add appointment");
                Console.WriteLine("2) List appointments");
                Console.WriteLine("3) Reschedule appointment");
                Console.WriteLine("4) Cancel appointment");
                Console.WriteLine("5) Save to CSV");
                Console.WriteLine("6) Load from CSV");
                Console.WriteLine("7) Write CSV template to inbox");
                Console.WriteLine("8) Exit");
                Console.Write("Select: ");

                string choice = Console.ReadLine();
                Console.WriteLine();
                try
                {
                    switch (choice)
                    {
                        case "1":
                            AddAppointmentFlow();
                            break;
                        case "2":
                            ListAppointments();
                            break;
                        case "3":
                            RescheduleFlow();
                            break;
                        case "4":
                            CancelFlow();
                            break;
                        case "5":
                            schedule.SaveCsv(DataCsvPath);
                            Console.WriteLine("Saved -> " + DataCsvPath);
                            break;
                        case "6":
                            int n = schedule.LoadCsv(DataCsvPath);
                            Console.WriteLine("Loaded {0} appointment(s) from -> {1}", n, DataCsvPath);
                            break;
                        case "7":
                            WriteTemplate();
                            Console.WriteLine("Template written to inbox: " + Path.Combine(InboxDir, "template.csv"));
                            break;
                        case "8":
                            return;
                        default:
                            Console.WriteLine("Unknown option.");
                            break;
                    }
                }
                catch (TimeConflictException ex)
                {
                    Console.WriteLine("❗ Time conflict: " + ex.Message);
                }
                catch (AppointmentNotFoundException ex)
                {
                    Console.WriteLine("❗ Not found: " + ex.Message);
                }
                catch (FormatException ex)
                {
                    Console.WriteLine("❗ Invalid input: " + ex.Message);
                }
                catch (IOException ex)
                {
                    Console.WriteLine("❗ File error: " + ex.Message);
                }
                catch (UnauthorizedAccessException ex)
                {
                    Console.WriteLine("❗ Access error: " + ex.Message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("❗ Unexpected error: " + ex.Message);
                }
            }
        }

        private static void AddAppointmentFlow()
        {
            Console.WriteLine("Type: 1) General  2) Doctor  3) Salon");
            Console.Write("Choose: ");
            string type = Console.ReadLine();

            Console.Write("Title: ");
            string title = Console.ReadLine();

            Console.Write("Start (yyyy-MM-dd HH:mm): ");
            DateTime start = DateTime.ParseExact(Console.ReadLine(), "yyyy-MM-dd HH:mm", null);

            Console.Write("End   (yyyy-MM-dd HH:mm): ");
            DateTime end = DateTime.ParseExact(Console.ReadLine(), "yyyy-MM-dd HH:mm", null);

            Console.Write("Client name: ");
            string cname = Console.ReadLine();

            Console.Write("Client phone: ");
            string cphone = Console.ReadLine();

            Appointment appt;
            if (type == "2")
            {
                Console.Write("Doctor name: ");
                string d = Console.ReadLine();
                appt = new DoctorAppointment(title, start, end, new Client(cname, cphone), d);
            }
            else if (type == "3")
            {
                Console.Write("Stylist name: ");
                string s = Console.ReadLine();
                appt = new SalonAppointment(title, start, end, new Client(cname, cphone), s);
            }
            else
            {
                appt = new Appointment(title, start, end, new Client(cname, cphone));
            }

            schedule.Add(appt);
            Console.WriteLine("Added: {0}", appt);
            Console.WriteLine("ID: {0}", appt.Id);
        }

        private static void ListAppointments()
        {
            List<Appointment> items = schedule.All();
            if (items.Count == 0)
            {
                Console.WriteLine("(no appointments)");
                return;
            }

            int i = 1;
            foreach (Appointment a in items)
            {
                Console.WriteLine("{0}. {1}", i++, a);
                Console.WriteLine("    ID: {0}", a.Id);
            }
        }

        private static Guid PromptId()
        {
            Console.Write("Enter Appointment ID (GUID): ");
            string idStr = Console.ReadLine();
            return Guid.Parse(idStr);
        }

        private static void RescheduleFlow()
        {
            ListAppointments();
            Guid id = PromptId();

            Console.Write("New Start (yyyy-MM-dd HH:mm): ");
            DateTime start = DateTime.ParseExact(Console.ReadLine(), "yyyy-MM-dd HH:mm", null);

            Console.Write("New End   (yyyy-MM-dd HH:mm): ");
            DateTime end = DateTime.ParseExact(Console.ReadLine(), "yyyy-MM-dd HH:mm", null);

            schedule.Reschedule(id, start, end);
            Console.WriteLine("Rescheduled.");
        }

        private static void CancelFlow()
        {
            ListAppointments();
            Guid id = PromptId();
            Console.Write("Reason (optional): ");
            string reason = Console.ReadLine();
            schedule.Cancel(id, reason);
            Console.WriteLine("Canceled.");
        }

        private static void WriteTemplate()
        {
            string path = Path.Combine(InboxDir, "template.csv");
            using (var sw = new StreamWriter(path, false, Encoding.UTF8))
            {
                sw.WriteLine("Id,Kind,Title,StartTime,EndTime,ClientName,ClientPhone,IsCanceled,CancelReason,ReminderSent,DoctorName,StylistName");
                sw.WriteLine(",General,Team Sync,2025-08-20 09:00,2025-08-20 09:30,Alice,0123456789,false,,false,,");
                sw.WriteLine(",Doctor,Consultation,2025-08-20 10:00,2025-08-20 10:30,Bob,0987654321,false,,false,Dr Smith,");
                sw.WriteLine(",Salon,Haircut,2025-08-20 11:00,2025-08-20 11:45,Carol,0820000000,false,,false,,Thandi");
            }
        }

        private static string ReadPassword()
        {
            StringBuilder sb = new StringBuilder();
            ConsoleKeyInfo key;
            while (true)
            {
                key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0)
                    {
                        sb.Length -= 1;
                        Console.Write("\b \b");
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    sb.Append(key.KeyChar);
                    Console.Write("*");
                }
            }
            return sb.ToString();
        }
    }
}
