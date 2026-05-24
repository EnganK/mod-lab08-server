using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ScottPlot;

namespace TPProj
{
	struct PoolRecord
	{
		public Thread thread;
		public bool in_use;
	}

	class procEventArgs : EventArgs
	{
		public int id { get; set; }
	}

	class Server
	{
		private PoolRecord[] pool;
		private object threadLock = new object();
		public int requestCount = 0;
		public int processedCount = 0;
		public int rejectedCount = 0;
		private int poolSize;
		private int serviceTimeMs;

		public Server(int n, int serviceTimeMs)
		{
			poolSize = n;
			this.serviceTimeMs = serviceTimeMs;
			pool = new PoolRecord[n];
		}

		public void Reset()
		{
			requestCount = 0;
			processedCount = 0;
			rejectedCount = 0;
			for (int i = 0; i < poolSize; i++)
			{
				pool[i].in_use = false;
				pool[i].thread = null;
			}
		}

		public void proc(object sender, procEventArgs e)
		{
			lock (threadLock)
			{
				requestCount++;
				for (int i = 0; i < poolSize; i++)
				{
					if (!pool[i].in_use)
					{
						pool[i].in_use = true;
						pool[i].thread = new Thread(new ParameterizedThreadStart(Answer));
						pool[i].thread.Start(e.id);
						processedCount++;
						return;
					}
				}
				rejectedCount++;
			}
		}

		public void Answer(object arg)
		{
			int id = (int)arg;
			Thread.Sleep(serviceTimeMs);
			lock (threadLock)
			{
				for (int i = 0; i < poolSize; i++)
					if (pool[i].thread == Thread.CurrentThread)
						pool[i].in_use = false;
			}
		}
	}

	class Client
	{
		private Server server;
		public event EventHandler<procEventArgs> request;

		public Client(Server server)
		{
			this.server = server;
			this.request += server.proc;
		}

		public void send(int id)
		{
			procEventArgs args = new procEventArgs { id = id };
			OnProc(args);
		}

		protected virtual void OnProc(procEventArgs e)
		{
			EventHandler<procEventArgs> handler = request;
			handler?.Invoke(this, e);
		}
	}

	class Program
	{
		static void Main()
		{
			int n = 3;
			int serviceTimeMs = 500;
			double mu = 1000.0 / serviceTimeMs;
			int totalRequestsPerRun = 100;

			double[] lambdas = new double[15];
			for (int i = 0; i < lambdas.Length; i++)
				lambdas[i] = 2 + i * 2;

			double[] p0Theory = new double[lambdas.Length];
			double[] pRejTheory = new double[lambdas.Length];
			double[] qTheory = new double[lambdas.Length];
			double[] aTheory = new double[lambdas.Length];
			double[] kTheory = new double[lambdas.Length];

			//double[] p0Exp = new double[lambdas.Length];
			double[] pRejExp = new double[lambdas.Length];
			double[] qExp = new double[lambdas.Length];
			double[] aExp = new double[lambdas.Length];
			double[] kExp = new double[lambdas.Length];

			Directory.CreateDirectory("result");

			for (int i = 0; i < lambdas.Length; i++)
			{
				double lambda = lambdas[i];
				Console.WriteLine($"Запуск для lambda = {lambda}");
				double intervalMs = 1000.0 / lambda;

				Server server = new Server(n, serviceTimeMs);
				Client client = new Client(server);

				Stopwatch sw = new Stopwatch();
				sw.Start();

				for (int id = 1; id <= totalRequestsPerRun; id++)
				{
					client.send(id);
					Thread.Sleep((int)intervalMs);
					if (id % 10 == 0) Console.WriteLine($"{id}/{totalRequestsPerRun}");
				}

				while (server.processedCount + server.rejectedCount < totalRequestsPerRun)
				{
					Thread.Sleep(50);
				}

				sw.Stop();
				double totalTimeSec = sw.Elapsed.TotalSeconds;

				double rho = lambda / mu;
				double sum = 0;
				for (int k = 0; k <= n; k++) sum += Math.Pow(rho, k) / Factorial(k);

				p0Theory[i] = 1.0 / sum;
				pRejTheory[i] = (Math.Pow(rho, n) / Factorial(n)) * p0Theory[i];
				qTheory[i] = 1.0 - pRejTheory[i];
				aTheory[i] = lambda * qTheory[i];
				kTheory[i] = aTheory[i] / mu;

				double pRejExpVal = (double)server.rejectedCount / totalRequestsPerRun;
				double qExpVal = (double)server.processedCount / totalRequestsPerRun;
				double aExpVal = lambda * qExpVal;
				double kExpVal = aExpVal / mu;
				//double p0ExpVal = 1.0 - (kExpVal / n);

				//p0Exp[i] = p0ExpVal;
				pRejExp[i] = pRejExpVal;
				qExp[i] = qExpVal;
				aExp[i] = aExpVal;
				kExp[i] = kExpVal;
			}

			GeneratePlot(lambdas, p0Theory, null, "Вероятность простоя", "result/p-1.png");
			GeneratePlot(lambdas, pRejTheory, pRejExp, "Вероятность отказа", "result/p-2.png");
			GeneratePlot(lambdas, qTheory, qExp, "Относительная пропускная способность", "result/p-3.png");
			GeneratePlot(lambdas, aTheory, aExp, "Абсолютная пропускная способность", "result/p-4.png");
			GeneratePlot(lambdas, kTheory, kExp, "Среднее число занятых каналов", "result/p-5.png");

			using (StreamWriter sw = new StreamWriter("results.txt", false, System.Text.Encoding.UTF8))
			{
				sw.WriteLine("Lambda\tP0_T\tPRej_T\tPRej_E\tQ_T\tQ_E\tA_T\tA_E\tK_T\tK_E");
				for (int i = 0; i < lambdas.Length; i++)
				{
					sw.WriteLine("{0:F2}\t{1:F4}\t{2:F4}\t{3:F4}\t{4:F4}\t{5:F4}\t{6:F4}\t{7:F4}\t{8:F4}\t{9:F4}",
						lambdas[i], p0Theory[i], pRejTheory[i], pRejExp[i],
						qTheory[i], qExp[i], aTheory[i], aExp[i], kTheory[i], kExp[i]);
				}
			}
		}

		static double Factorial(int n)
		{
			double res = 1;
			for (int i = 1; i <= n; i++) res *= i;
			return res;
		}

		static void GeneratePlot(double[] xs, double[] ysTheory, double[] ysExp, string title, string filename)
		{
			var plt = new Plot();
			plt.Title(title);
			plt.XLabel("Интенсивность входного потока (lambda)");
			plt.YLabel("Значение");

			var theory = plt.Add.Scatter(xs, ysTheory);
			theory.LegendText = "Теория";
			if (ysExp != null)
			{
				var exp = plt.Add.Scatter(xs, ysExp);
				exp.LegendText = "Эксперимент";
			}
			plt.SavePng(filename, 600, 400);
		}
	}
}