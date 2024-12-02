namespace Cosmos.Samples.Shared
{
   using System;
   using System.Collections.Generic;
   using System.IO;
   using System.Threading.Tasks;
   using Microsoft.Azure.Cosmos.Fluent;
   using Microsoft.Azure.Cosmos;
   using Newtonsoft.Json;
   using System.Diagnostics;

    public class HybridSearch 
    {
        private static class Queries
        {
	    private static readonly bool useTop = true;
	    private static readonly int top = 1000;
	    private static readonly string topStr = useTop ? "TOP " + top : "";
	    private static readonly string embedding = CreateEmbedding();
            public static readonly Query fts = new Query("", "SELECT " + topStr + " c.id AS Text FROM c WHERE FullTextContains(c.text, 'shoulder')");
            public static readonly Query ftr = new Query("", "SELECT " + topStr + " c.id AS Text FROM c Order By Rank FullTextScore(c.text, ['may', 'music'])"); 
            public static readonly Query hs = new Query("", "SELECT " + topStr + " c.id AS Text FROM c Order By Rank RRF(FullTextScore(c.text, ['may', 'music']), VectorDistance(c.vector," + embedding + "))");
        }

	public static string CreateEmbedding()
	{
    		Random random = new Random();
    		List<double> randomNumbers = new List<double>();
    		for (int i = 0; i < 128; i++)
    		{
        		double randomNumber = -1 + (1 - (-1)) * random.NextDouble();
        		randomNumbers.Add(randomNumber);

    		}
		return "[" + String.Join(", ", randomNumbers) + "]";

	}

        public sealed class Query
        {
            public Query(string description, string text)
            {
                this.Description = description;
                this.Text = text;
            }

            public string Text { get; }
            public string Description { get; }

            public override string ToString()
            {
                return this.Description;
            }
        }

	public record Doc 
	{
	   public Doc(string pk, List<double> embedding, string text, string id) {
		   this.pk = pk;
		   this.text = text;
		   this.id = id;
		   this.embedding = embedding;
	   }

	   public string pk { get;}
	   public List<double> embedding { get;}
	   public string text { get; } 
	   public string id { get;}

	}

        private static readonly string accountEndpoint = ""; // insert your endpoint here.
        private static readonly string accountKey = ""; // insert your key here.

        public HybridSearch()
        {
            
        }


        public static async Task Main(string[] args)
        {
            CosmosClientBuilder clientBuilder = new CosmosClientBuilder(
                accountEndpoint: accountEndpoint,
                authKeyOrResourceToken: accountKey);

            CosmosClient client = clientBuilder.WithConnectionModeGateway().Build();
            Database db = client.GetDatabase("perf-tests-sdks");
            Container container = db.GetContainer("fts");
            bool latency = true;
	    bool diagnostics = true;
	    int queryIndex = 1;
	    int warmUp = 0; //120
	    int testTime = 30; // 600
	    Console.WriteLine("Latency: " + latency);
	    Console.WriteLine("Diagnostics: " + diagnostics);
	    Console.WriteLine("QueryIndex: " + queryIndex);
	    string query = Queries.fts.Text;
	    double totalRUCharge = 0;
	    if (queryIndex == 1) {
		    query = Queries.ftr.Text;
	    } else if (queryIndex == 2) {
		    query = Queries.hs.Text;
	    }
	    Console.WriteLine(Queries.hs.Text);

            
            //Stopwatch is designed for this purpose and is one of the best ways to measure time execution in .NET.

            if (!latency) {
                GC.GetTotalMemory(true);
                Console.WriteLine(GC.GetTotalMemory(false));
                for (int i = 0; i < 50; i++) {
                    using FeedIterator<Doc> feedIterator = container.GetItemQueryIterator<Doc>(
                        queryText: query);
                    while (feedIterator.HasMoreResults)
                    {
                        FeedResponse<Doc> responseMessage = await feedIterator.ReadNextAsync(); 
                    }
                }
                Console.WriteLine(GC.GetTotalMemory(false));
                Console.WriteLine(GC.GetTotalMemory(true));
            }
	    else {
		string diagnosticsStr = "";

		Stopwatch timer = new Stopwatch();
		timer.Start();
		while (timer.Elapsed < TimeSpan.FromSeconds(warmUp)) {
                    using FeedIterator<Doc> feedIterator = container.GetItemQueryIterator<Doc>(
                        queryText: query, new QueryRequestOptions { MaxConcurrency = -1});
                    while (feedIterator.HasMoreResults)
                    {
                        FeedResponse<Doc> responseMessage = await feedIterator.ReadNextAsync(); 
			//Console.WriteLine("Recieved Response");
			
                    }
                }
		Console.WriteLine("Warm up finished");
            	List<long> times = new List<long>();
		timer.Restart();
        	while (timer.Elapsed < TimeSpan.FromSeconds(testTime)) {
                	using FeedIterator<Doc> feedIterator = container.GetItemQueryIterator<Doc>(
                    	queryText: query);
                	var watch = Stopwatch.StartNew();
                	while (feedIterator.HasMoreResults)
                	{
                    		FeedResponse<Doc> responseMessage = await feedIterator.ReadNextAsync();
				totalRUCharge += responseMessage.RequestCharge;
				if (diagnostics) {
					diagnosticsStr += responseMessage.Diagnostics.ToString() + "\n --------- \n";
				}
                	}
                	watch.Stop();
                	var elapsedMs = watch.ElapsedMilliseconds;
//                	Console.WriteLine(elapsedMs);
                	times.Add(elapsedMs);
            	}
		timer.Stop();
		Console.WriteLine("Rus: " + totalRUCharge);
		if (diagnostics) {
			using (StreamWriter outputFile = new StreamWriter("dotnet-diagnostics.txt"))
    			outputFile.WriteLine(diagnosticsStr);
		}
            	double sum = 0;
            	for (int i = 0; i < times.Count; i++) {
                	sum += times[i];
            	}
            	Console.WriteLine("average latency " + sum / times.Count + " ms");
		Console.WriteLine("Number of queries: " + times.Count); 
	    }


        }



    }
}
