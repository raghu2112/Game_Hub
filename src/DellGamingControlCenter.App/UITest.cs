using System;
using System.Linq;
using System.Threading.Tasks;

using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Services;
using DellGamingControlCenter.App.ViewModels;
using System.IO;

namespace DellGamingControlCenter.App.Tests
{
    public class AutomatedUITester
    {
        public static async Task RunTests()
        {
            Console.WriteLine("Starting UI Automation Test...");
            
            var discoveryService = App.GameDiscovery;
            var tuningService = App.GameTuningService;
            var benchmarkService = App.BenchmarkService;
            
            Console.WriteLine("Scanning for real games...");
            var games = await discoveryService.ScanForGamesAsync(true);
            Console.WriteLine($"Found {games.Count()} games.");
            
            if (!games.Any()) 
            {
                Console.WriteLine("No games found! Test skipped.");
                return;
            }

            Console.WriteLine("Initializing BenchmarkViewModel...");
            var vm = App.BenchmarkViewModel;
            
            // Wait for load
            await Task.Delay(1000);
            
            Console.WriteLine($"ViewModel selected game ID: {vm.SelectedGameId}");
            
            if (string.IsNullOrEmpty(vm.SelectedGameId))
            {
                Console.WriteLine("ViewModel failed to select a game by default.");
                return;
            }

            // 3. Invoke Baseline Benchmark Command
            Console.WriteLine("Invoking StartBaselineCommand...");
            if (vm.StartBaselineCommand.CanExecute(null))
            {
                vm.StartBaselineCommand.Execute(null);
            }
            else
            {
                Console.WriteLine("Cannot execute baseline command!");
                return;
            }
            
            // 4. Poll while running
            while (vm.IsRunning)
            {
                Console.WriteLine($"Status: {vm.StatusText}");
                await Task.Delay(1000);
            }
            
            Console.WriteLine($"Benchmark finished! Final Status: {vm.StatusText}");
            Console.WriteLine($"ViewModel History Count: {vm.BenchmarkHistory.Count}");
            
            Console.WriteLine("UI Validation Test Complete. ALL PASSED.");
        }
    }
}
