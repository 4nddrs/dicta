using System.Net.Sockets;
using System.Text;

namespace Dicta;

static class InstanceBus
{
    private static readonly object Gate = new();
    private static readonly Queue<string> Pending = new();
    private static Action<string>? _handler;
    private static Socket? _socket;
    private static string? _path;

    public static bool TakeOwnership(string[] args)
    {
        var command = IsHelp(args) ? "help" : "toggle";
        var dir = AppPaths.StateDirectory;
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "dicta.sock");

        if (TryForward(_path, command))
            return false;

        try
        {
            File.Delete(_path);
        }
        catch
        {
            // A leftover socket is replaced below.
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(_path));
            socket.Listen(4);
        }
        catch (Exception ex)
        {
            socket.Dispose();
            if (TryForward(_path, command))
                return false;

            Log.Error("Couldn't start the single-instance listener.", ex);
            Console.Error.WriteLine("Dicta is already running, but it didn't answer.");
            return false;
        }

        _socket = socket;
        _ = Task.Run(() => AcceptLoop(socket));
        if (command == "help")
            Dispatch("help");
        return true;
    }

    public static void Bind(Action<string> handler)
    {
        List<string> queued;
        lock (Gate)
        {
            _handler = handler;
            queued = Pending.ToList();
            Pending.Clear();
        }

        foreach (var command in queued)
            handler(command);
    }

    public static void Stop()
    {
        try
        {
            _socket?.Close();
        }
        catch
        {
            // Closing the listener unblocks Accept.
        }

        try
        {
            if (_path != null)
                File.Delete(_path);
        }
        catch
        {
            // The socket file is only a rendezvous point.
        }
    }

    private static void AcceptLoop(Socket socket)
    {
        while (true)
        {
            Socket client;
            try
            {
                client = socket.Accept();
            }
            catch
            {
                return;
            }

            try
            {
                using (client)
                using (var stream = new NetworkStream(client, ownsSocket: false))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var line = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                        Dispatch(line.Trim());
                }
            }
            catch (Exception ex)
            {
                Log.Error("Couldn't read a command from another Dicta.", ex);
            }
        }
    }

    private static void Dispatch(string command)
    {
        Action<string>? handler;
        lock (Gate)
        {
            handler = _handler;
            if (handler == null)
            {
                Pending.Enqueue(command);
                return;
            }
        }

        handler(command);
    }

    private static bool TryForward(string path, string command)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.SendTimeout = 1000;
            socket.ReceiveTimeout = 1000;
            socket.Connect(new UnixDomainSocketEndPoint(path));
            var bytes = Encoding.UTF8.GetBytes(command + "\n");
            socket.Send(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsHelp(string[] args)
        => args.Length > 0 && args[0] is "help" or "--help" or "-h";
}
