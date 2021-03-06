﻿using Common;
using System;
using System.Linq;
using System.ServiceModel;
using System.Threading;

namespace Compute
{
    public class Program
    {
        public static int numberOfActiveContainers = 0;

        private static int roleServerStartingPort = 15000;

        private static void Main()
        {
            Console.WriteLine("Press to start process");
            Console.ReadKey(true);

            if (ComputeConfigurationManipulator.LoadConfiguration() is null)
            {
                Console.WriteLine("Nevalidna konfiguracija");
                Console.ReadKey();
                return;
            }

            var roleServer = new Server(port: roleServerStartingPort, serverType: typeof(RoleEnviroment), interfaceType: typeof(IRoleEnvironment));
            roleServer.Open();

            var scaleServer = new Server(port: roleServerStartingPort + 10, serverType: typeof(ComputeMenagment), interfaceType: typeof(IComputeManagement));
            scaleServer.Open();

            // clear old containers
            DllMenager.ClearContainersOfOldDlls();

            // kopiraj kontejner na 4 lokacije
            ContainerFactory.Instance.CopyContainersTo4Locations();

            // kopiraj dll-ove na n lokacija
            DllMenager.CopyDllsToNContainers(ComputeConfigurationManipulator.LoadConfiguration().Value);
            
            // startuj kontejnere
            ContainerFactory.Instance.CreateAndStartContainers(roleServerStartingPort);

            // ako imaja dodatnih dll-ova pokrenuti ih
            DllMenager.LoadDllToContainers();

            // periodicno proverava da li se na predefinisanoj lokaciji nalazi novi paket
            new FileWatcher().StartWatching();

            var t = new Thread(CheckHealth) { IsBackground = true };
            t.Start();

            Console.WriteLine("Press key to abort all processes");
            Console.ReadKey(true);

            t.Abort();
            ContainerFactory.Instance.KillAllLiveContainers();

            roleServer.Close();
            scaleServer.Close();

            Console.WriteLine("Press key to exit");
            Console.ReadKey(true);
        }

        private static void CheckHealth()
        {
            while (true)
            {
                Console.WriteLine("Checking health of containers...");
                foreach (var item in ContainerFactory.Instance.Containers)
                {
                    try
                    {
                        using (var factory = new ChannelFactory<IContainerManagement>(new NetTcpBinding(), item.Value))
                        {
                            var proxy = factory.CreateChannel();
                            Console.Write($"{proxy.CheckHealth()} ");
                        }
                    }
                    catch (EndpointNotFoundException)
                    {
                        Console.WriteLine($"------------------------- Endpoint not found --------------{item.Value}");
                        Console.WriteLine($"Trying to reboot container on address {item.Value} ...");
                        ContainerFactory.Instance.RestartContainer(item.Key);
                        DllMenager.LoadDllToContainers();
                    }
                    catch (CommunicationObjectFaultedException)
                    {
                        Console.WriteLine($"Coontainer on address {item.Value} not responding.");
                        Console.WriteLine("Killing container...");
                        ContainerFactory.Instance.KillContainer(item.Key);
                        Console.WriteLine("Restarting container...");
                        ContainerFactory.Instance.RestartContainer(item.Key);
                    }
                }
                Console.WriteLine();
                Thread.Sleep(5000);
            }
        }
    }
}
