﻿namespace Client
{
    using System;
    using Microsoft.AspNetCore.SignalR.Client;
    using System.Threading.Tasks;
    using System.Diagnostics;
    using System.Threading;
    using System.Collections.Generic;
    using System.Linq;

    class Program
    {
        class Counter
        {
            DateTime window = DateTime.MinValue;

            DateTime begin = DateTime.MinValue;

            long allSum = 0;

            int allCount = 0;

            List<long> data = new List<long>();

            int unit;

            string name;

            DateTime Floor(DateTime dt)
            {
                var sec = dt.Minute * 60 + dt.Second;
                var floored = sec / unit * unit;
                return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, floored / 60, floored % 60);
            }

            public Counter(string name, int unit)
            {
                this.name = name;
                this.unit = unit;
            }

            public void Add(long value)
            {
                List<long> last = null;
                lock (this)
                {
                    var now = Floor(DateTime.UtcNow);
                    if (begin == DateTime.MinValue) begin = now;
                    if (window != DateTime.MinValue && window != now)
                    {
                        last = data;
                        data = new List<long>();
                    }

                    window = now;
                    data.Add(value);
                }

                if (last != null)
                {
                    last.Sort();
                    var count = last.Count();
                    var sum = last.Sum();
                    allCount += count;
                    allSum += sum;
                    long avg = sum / count, d95 = last[(int)(count * 0.95)], d99 = last[(int)(count * 0.99)];
                    Console.WriteLine($"[{window}] {name}: 99%: {d99}ms, 95%: {d95}ms, avg: {sum / count}ms, count: {count}");
                }
            }
        }

        static Counter received;

        static Counter sent;

        static HubConnection[] connection;

        static Random rand = new Random((int)DateTime.Now.Ticks);

        static Stopwatch sw = new Stopwatch();

        static string endpoint;

        static int count;

        static int sendInterval;

        static int statInterval;


        static async Task Connect()
        {
            Stopwatch w = new Stopwatch();
            w.Start();
            connection = new HubConnection[count];
            for (var i = 0; i < count; i++)
            {
                connection[i] = new HubConnectionBuilder().WithUrl(endpoint).Build();
                try
                {
                    await connection[i].StartAsync();
                    connection[i].On<long, string>("Pong", (started, message) => received.Add(sw.ElapsedMilliseconds - started));
                }
                catch (Exception ex)
                {
                    i--;
                    Console.WriteLine("Connect failed with the following message:");
                    Console.WriteLine(ex.Message);
                    Console.WriteLine("Retry in one second.");
                    await Task.Delay(1000);
                }
                if ((i + 1) % 100 == 0)
                {
                    Console.WriteLine($"{i + 1} connected, time elapsed: {w.Elapsed}");
                }
            }
        }

        static async Task SendOne(HubConnection c)
        {
            long started = sw.ElapsedMilliseconds;
            await c.InvokeAsync("Ping", started, "World");
            var elapsed = sw.ElapsedMilliseconds - started;
            sent.Add(elapsed);
            var delay = sendInterval - (int)elapsed;
            if (delay > 0) await Task.Delay(delay);
        }

        static async Task Send(HubConnection c)
        {
            // Wait for a random time
            await Task.Delay(rand.Next(sendInterval));
            while (true) await SendOne(c);
        }

        static void SendSync(object o)
        {
            WaitHandle h = new ManualResetEvent(false);
            HubConnection c = (HubConnection)o;
            // Wait for a random time
            h.WaitOne(rand.Next(sendInterval));
            c.On<long, string>("Pong", (started, message) => received.Add(sw.ElapsedMilliseconds - started));
            while (true)
            {
                long started = sw.ElapsedMilliseconds;
                c.InvokeAsync("Ping", started, "World").Wait();
                var elapsed = sw.ElapsedMilliseconds - started;
                sent.Add(elapsed);
                var delay = sendInterval - (int)elapsed;
                if (delay > 0) h.WaitOne(delay);
            }
        }

        static void RunSync()
        {
            Connect().Wait();
            sw.Start();
            Thread[] ts = new Thread[count];
            for (var i = 0; i < count; i++)
            {
                ts[i] = new Thread(SendSync);
                ts[i].Start(connection[i]);
            }
            Console.ReadKey();
        }

        static async Task Run()
        {
            await Connect();
            sw.Start();
            var ts = new Task[count];
            for (var i = 0; i < count; i++) ts[i] = Send(connection[i]);
            Console.ReadKey();
        }

        static async Task Run2()
        {
            await Connect();
            sw.Start();
            Task[] ts = new Task[count];
            for (var i = 0; i < ts.Length; i++) ts[i] = SendOne(connection[i]);
            for (;;)
            {
                var i = Task.WaitAny(ts);
                ts[i] = SendOne(connection[i]);
            }
        }

        static void Main(string[] args)
        {
            if (args.Length < 4)
            {
                Console.WriteLine("Usage: client [server_endpoint] [connection_count] [send_interval(ms)] [stat_interval(sec)]");
                return;
            }

            Console.WriteLine("Start at " + DateTime.Now + ", press any key to stop...");
            endpoint = args[0];
            count = int.Parse(args[1]);
            sendInterval = int.Parse(args[2]);
            statInterval = int.Parse(args[3]);
            sent = new Counter("send", statInterval);
            received = new Counter("recv", statInterval);
            Run().Wait();
            // RunSync();
            // Run2().Wait();
        }
    }
}
