using AppointmentScheduler.Core;
using AppointmentScheduler.Core.Exceptions;
using AppointmentScheduler.Core.Policies;
using AppointmentScheduler.Core.Repositories;
using AppointmentScheduler.Models;
using AppointmentScheduler.Services;

namespace AppointmentScheduler
{
    internal class Program
    {
        static void Main()
        {
            // Security/least-privilege: keep data under local "data" directory
            string dataRoot = Path.Combine(AppContext.BaseDirectory, "data");
            string repoPath = Path.Combine(dataRoot, "appointments.json");
            string watchDir = Path.Combine(dataRoot, "Appointments");
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(watchDir);


            IAppointmentRepository repository = new FileAppointmentRepository(repoPath);
            IConflictPolicy conflictPolicy = new SimpleOverlapPolicy();


            var schedule = new Schedule(conflictPolicy);
            var notifier = new AppointmentNotifier();
            var monitor = new AppointmentMonitor(schedule);
            var watcher = new DirectoryWatcher(watchDir, schedule, repository);


            // Subscribe to events
            schedule.AppointmentAdded += (s, a) => Console.WriteLine($"[ADDED] {a}");
            schedule.AppointmentRemoved += (s, a) => Console.WriteLine($"[REMOVED] {a}");
            notifier.AppointmentNear += (s, a) => Console.WriteLine($"[REMINDER] {a.Subject} at {a.Start:t} for {a.Client}");
            monitor.AppointmentNear += notifier.RelayNearEvent; // decoupled relay
            repository.SaveFailed += (s, msg) => Console.WriteLine($"[SAVE ERROR] {msg}");
            watcher.FileEvent += (s, msg) => Console.WriteLine($"[WATCH] {msg}");


            // Load persisted items (if any)
            foreach (var appt in repository.Load())
                schedule.TryAdd(appt, out _); // ignore conflicts on startup; you can change this behavior


            monitor.Start();
            watcher.Start();


            Console.WriteLine("\n=== Appointment Scheduler ===");
            Console.WriteLine("Commands: add | list | remove | save | help | exit");
            Console.WriteLine("Directory monitor: drop *.appt files into 'data/Appointments' to auto-import.");
            Console.WriteLine("Example .appt JSON: {\"Subject\":\"Cut\",\"Client\":\"Sam\",\"Start\":\"2025-08-19T13:30:00\",\"End\":\"2025-08-19T14:00:00\",\"Type\":\"Salon\"}");


            while (true)
            {
                Console.Write("\n> ");
                var line = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cmd = line.Trim().ToLowerInvariant();


                try
                {
                    switch (cmd)
                    {
                        case "add":
                            AddFlow(schedule);
                            break;
                        case "list":
                            foreach (var a in schedule.GetAll()) Console.WriteLine(a);
                            break;
                        case "remove":
                            RemoveFlow(schedule);
                            break;
                        case "save":
                            repository.Save(schedule.GetAll());
                            Console.WriteLine("Saved.");
                            break;
                        case "help":
                            Console.WriteLine("add -> guided add; list -> show; remove -> by id; save -> persist; exit -> quit");
                            break;
                        case "exit":
                            monitor.Stop();
                            watcher.Stop();
                            repository.Save(schedule.GetAll());
                            Console.WriteLine("Goodbye.");
                            return;
                        default:
                            Console.WriteLine("Unknown command. Type 'help'.");
                            break;
                    }
                }
                catch (FormatException ex)
                {
                    Console.WriteLine($"[FORMAT] {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] {ex.Message}");
                }
            }
        }


        private static void AddFlow(Schedule schedule)
        {
            Console.Write("Subject: ");
            var subject = ReadNonEmpty();


            Console.Write("Client: ");
            var client = ReadNonEmpty();


            Console.Write("Type (Salon/Doctor/Other): ");
            if (!Enum.TryParse<AppointmentType>(Console.ReadLine(), true, out var type))
                throw new FormatException("Type must be Salon, Doctor, or Other.");


            Console.Write("Start (yyyy-MM-dd HH:mm): ");
            if (!DateTimeOffset.TryParse(Console.ReadLine(), out var start))
                throw new FormatException("Invalid start date/time.");


            Console.Write("End (yyyy-MM-dd HH:mm): ");
            if (!DateTimeOffset.TryParse(Console.ReadLine(), out var end))
                throw new FormatException("Invalid end date/time.");


            var appt = new Appointment(subject, client, start, end, type);
            if (!schedule.TryAdd(appt, out var reason))
                throw new TimeConflictException(reason ?? "Unknown conflict.");


            Console.WriteLine("Added.");
        }


        private static void RemoveFlow(Schedule schedule)
        {
            Console.Write("Enter Id (GUID): ");
            if (!Guid.TryParse(Console.ReadLine(), out var id))
                throw new FormatException("Id must be a GUID.");


            if (schedule.Remove(id)) Console.WriteLine("Removed.");
            else Console.WriteLine("Not found.");
        }


        private static string ReadNonEmpty()
        {
            var s = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(s)) throw new Core.Exceptions.InvalidAppointmentException("Value cannot be empty.");
            return s.Trim();
        }
    }
}
