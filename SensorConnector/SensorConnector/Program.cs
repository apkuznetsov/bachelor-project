﻿using SensorConnector.CommandLineArgsParser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vibrant.InfluxDB.Client;
using Timer = System.Timers.Timer;

namespace SensorConnector
{
    public class Program
    {
        private static string _executionParamsStringExample =
            "-testId 132 -executionTime 20 -sensors 127.0.0.1:1111 127.0.0.1:2222 127.2.2.2:3333";

        private static int LISTEN_PORT = 8888; // local port for listening incoming data

        private static int _testId;
        private static int _programExecutionTime; // in seconds
        private static List<Sensor> _sensors;

        private static InfluxClient _client;
        private static string _databaseName = "dms_influx_db";
        private static string _measurementNameBase = "sensor_outputs_test_";
        private static string _measurementName;

        private static Timer _timer;

        static int Main(string[] args) => MainAsync(args).GetAwaiter().GetResult();

        static async Task<int> MainAsync(string[] args)
        {
            // Console.WriteLine("GetCommandLineArgs: {0}", string.Join(", ", args));

            const string influxHost = "http://localhost:8086";

            ParsedParamsDto parsedInputParams;

            try
            {
                #region Test

                // WARNING: Remove or comment out this part for production.
                var testArgs = _executionParamsStringExample.Split(' ');
                args = testArgs;

                #endregion Test


                parsedInputParams = CommandLineArgsParser.CommandLineArgsParser.ParseInputParams(args);
            }
            catch (Exception ex)
            {
                var errorMessage = "ERROR: execution params parsing failed.\r\n" + ex.Message;
                Console.WriteLine(errorMessage);

                return 1;
            }

            _testId = parsedInputParams.TestId;
            _programExecutionTime = parsedInputParams.ProgramExecutionTime;
            _sensors = parsedInputParams.Sensors;

            _measurementName = _measurementNameBase + _testId;

            _client = new InfluxClient(new Uri(influxHost));

            // var databases = await _client.ShowDatabasesAsync();

            await _client.CreateDatabaseAsync(_databaseName); // creates db if not exist

            InitTimer(_programExecutionTime);

            try
            {
                Console.WriteLine("Listening to port: {0}...", LISTEN_PORT);

                Thread receiveThread = new Thread(new ThreadStart(ReceiveMessage));
                receiveThread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);

                return 2;
            }

            return 0;
        }

        private static void ReceiveMessage()
        {
            UdpClient receiver = new UdpClient(LISTEN_PORT); // UdpClient for receiving incoming data

            IPEndPoint remoteIp = null; // address of the sending server (NULL means Any)

            try
            {
                // Start the timer
                _timer.Enabled = true;

                while (true)
                {
                    byte[] data = receiver.Receive(ref remoteIp); // receive data from the server

                    var senderIpAddress = remoteIp.Address.ToString();
                    var senderPort = remoteIp.Port;

                    Console.WriteLine($"Received broadcast from {remoteIp}");

                    if (IsTargetSensor(senderIpAddress, senderPort))
                    {
                        var dataAsStr = Encoding.ASCII.GetString(data, 0, data.Length);

                        WriteReceivedBatchToInfluxDbAsync(senderIpAddress, senderPort, dataAsStr)
                             .ContinueWith(t =>
                             {
                                 Console.WriteLine($"Wrote broadcast from {remoteIp} to InfluxDB");

                             });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static bool IsTargetSensor(string ip, int port)
        {
            return _sensors.FirstOrDefault(
                x => x.IpAddress.Equals(ip) && x.Port == port) != null;
        }

        private static async Task WriteReceivedBatchToInfluxDbAsync(string sensorIpAddress, int sensorPort, string data)
        {
            // TODO: Implement buffered writing
            var sensorOutput = new SensorOutput
            {
                Timestamp = DateTime.Now,
                SensorIpAddress = sensorIpAddress,
                SensorPort = sensorPort,
                Data = data
            };

            var outputs = new List<SensorOutput>() { sensorOutput };

            await Task.Delay(2000);

            await _client.WriteAsync(_databaseName, _measurementName, outputs);
        }

        /// <summary>
        /// Initializes timer to countdown program execution time.
        /// </summary>
        /// <param name="programExecutionTime">Amount of time in seconds for which program will listen to sensors.</param>
        private static void InitTimer(int programExecutionTime)
        {
            // Create a timer its countdown time in milliseconds.
            _timer = new Timer
            {
                Interval = programExecutionTime * 1000
            };

            // Hook up the Elapsed event for the timer. 
            _timer.Elapsed += OnTimedEvent;
        }

        private static void OnTimedEvent(Object source, System.Timers.ElapsedEventArgs e)
        {
            Console.WriteLine($"Execution time ended.");

            Environment.Exit(0);
        }
    }
}