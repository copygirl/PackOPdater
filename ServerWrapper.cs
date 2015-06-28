using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PackOPdater
{
	public class ServerWrapper
	{
		string _directory, _arguments;

		Process _process;
		StreamWriter _input;

		public bool Running { get { return (_process != null); } }
		public bool Done { get; private set; }
		public bool AutoRestart { get; set; }

		public ICollection<string> Players { get; private set; }

		public event Action<string> Output;

		public ServerWrapper(string directory, string file, string[] arguments)
		{
			_directory = directory;
			_arguments = string.Join(" ", arguments.Concat(new[]{ "-jar", file, "nogui" }).Select(
				arg => (arg.Contains(' ') ? '"' + arg + '"' : arg)));
			
			Players = new HashSet<string>();

			Console.CancelKeyPress += (sender, e) => {
				if (!Running) return;
				AutoRestart = false;
				Stop().Wait();
				e.Cancel = true;
			};
		}

		public void Start()
		{
			if (Running)
				throw new InvalidOperationException("Server process already running");

			var info = new ProcessStartInfo {
				WorkingDirectory = _directory,
				FileName = DetectJavaPath(), Arguments = _arguments,
				UseShellExecute = false, CreateNoWindow = true,
				RedirectStandardOutput = true, RedirectStandardInput = true,
			};

			_process = Process.Start(info);
			_input = _process.StandardInput;

			_process.Exited += (sender, e) => OnExit();
			_process.OutputDataReceived += (sender, e) => OnOutput(e.Data);

			_process.BeginOutputReadLine();
		}

		static string DetectJavaPath()
		{
			var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
			if (javaHome != null) {
				var javaPath = Path.Combine(javaHome, "bin", "java.exe");
				if (File.Exists(javaPath))
					return javaPath;
			}

			return Environment.GetEnvironmentVariable("PATH").Split(';')
				.Select(p => Path.Combine(p, "java.exe"))
				.FirstOrDefault(File.Exists) ?? "java";
		}

		public async Task Stop()
		{
			if (!Running)
				throw new InvalidOperationException("Server process not running");

			var timer = TimeSpan.FromSeconds(64);
			var noticeInterval = TimeSpan.FromSeconds(8);

			while ((timer.Seconds > 0) && (Players.Count > 0)) {
				Input(@"/tellraw @p [{""text"":""Server updating in " + timer.TotalSeconds + @" seconds..."",""color"":""yellow"",""bold"":""false""}]");
				await Task.Delay(noticeInterval);
				timer -= noticeInterval;
			}

			Input("stop");

			var waitTask = Task.Delay(TimeSpan.FromSeconds(10));
			var exitTask = Task.Run(() => _process.WaitForExit());

			var task = await Task.WhenAny(waitTask, exitTask);
			if (task == waitTask)
				_process.Kill();
		}

		public void Input(string line)
		{
			if (!Running)
				throw new InvalidOperationException("Server process not running");
			
			_input.WriteLine(line);
		}

		void OnExit()
		{
			Console.WriteLine("Server stopped!");

			_process.Dispose();
			_process = null;
			Done = false;
			Players.Clear();

			if (AutoRestart)
				Start();
		}

		static readonly string prefix = @"^\[..:..:..\] \[Server thread/INFO\]: ";
		static readonly Regex _doneRegex  = new Regex(prefix + @"Done \(.*s\)! For help, type ""help"" or ""\?""$");
		static readonly Regex _joinRegex  = new Regex(prefix + @"(§.)?(?<name>.*)(§.)? joined the game$");
		static readonly Regex _leaveRegex = new Regex(prefix + @"(§.)?(?<name>.*)(§.)? left the game$");

		void OnOutput(string line)
		{
			if (line == null) {
				OnExit();
				return;
			}

			if (!Running && _doneRegex.IsMatch(line))
				Done = true;

			var joinMatch = _joinRegex.Match(line);
			if (joinMatch.Success)
				Players.Add(joinMatch.Groups["name"].Value);

			var leaveMatch = _leaveRegex.Match(line);
			if (leaveMatch.Success)
				Players.Remove(joinMatch.Groups["name"].Value);

			if (Output != null)
				Output(line);
		}
	}
}

