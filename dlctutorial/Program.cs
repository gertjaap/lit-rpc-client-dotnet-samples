using System;
using System.IO;
using Mit.Dci.Lit;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace tutorial
{
    class Program
    {
        // The variables for the contract
        private static byte[] oraclePubKey = StringToByteArray("03c0d496ef6656fe102a689abc162ceeae166832d826f8750c94d797c92eedd465");
        private static byte[] rPoint = StringToByteArray("027168bba1aaecce0500509df2ff5e35a4f55a26a8af7ceacd346045eceb1786ad");
        private static long oracleValue = 15161;
        private static byte[] oracleSig = StringToByteArray("9e349c50db6d07d5d8b12b7ada7f91d13af742653ff57ffb0b554170536faeac");

        // Construct LIT nodes
        static LitClient lit1 = new LitClient("localhost",8001);
        static LitClient lit2 = new LitClient("localhost",8002);

        static void Main(string[] args)
        {
            Run().Wait();
        }

        
        static async Task Run() {
            try {
                // Connect both LIT peers together
                Console.WriteLine("Connecting nodes together...");
                await ConnectNodes();

                // Find out if the oracle is present and add it if not
                Console.WriteLine("Ensuring oracle is available...");
                var oracleIdxs = await CheckOracle();

                // Create the contract and set its parameters
                Console.WriteLine("Creating the contract...");
                var contract = await CreateContract(oracleIdxs[0]);
                        
                // Offer the contract to the other peer
                Console.WriteLine("Offering the contract to the other peer...");
                await lit1.OfferContract(contract.Idx, 1);

                // Wait for the contract to be exchanged
                Console.WriteLine("Waiting for the contract to be exchanged...");
                Thread.Sleep(2000);

                // Accept the contract on the second node
                Console.WriteLine("Accepting the contract on the other peer...");
                await AcceptContract();

                // Wait for the contract to be activated
                Console.WriteLine("Waiting for the contract to be activated...");
                while(!await IsContractActive(contract.Idx)) 
                    Thread.Sleep(1000);

                Console.WriteLine("Contract active. Generate a block on regtest and press enter");
                Console.ReadLine();

                // Settle the contract
                Console.WriteLine("Settling the contract...");
                await lit1.SettleContract(contract.Idx, oracleValue, oracleSig);

                Console.WriteLine("Contract settled. Mine two blocks to ensure contract outputs are claimed back to the nodes' wallets.\r\n\r\nDone.");

            } catch (Exception e) {
                Console.WriteLine("Error occurred: {0}", e.ToString());
            }
        }
        
        // Connect the two nodes together
        static async Task ConnectNodes() {
            // Connect to both LIT nodes
            await lit1.Open();
            await lit2.Open();

            // Instruct both nodes to listen for incoming connections
            try { 
                 await lit1.Listen();
            } catch (Exception ex) {
                Console.WriteLine("Ex: {0}", ex);
            }
            await lit2.Listen();

            // Connect node 1 to node 2
            var lnAdr = await lit2.GetLNAddress();
            await lit1.Connect(lnAdr,"localhost:2449");
        }

        static async Task<int[]> CheckOracle() {
            // Fetch a list of oracles from both nodes
            var oracles1 = await lit1.ListOracles();
            var oracles2 = await lit2.ListOracles();

            // Find the oracle we need in both lists
            var oracle1 = oracles1.FirstOrDefault(o => o.A.SequenceEqual(oraclePubKey));
            var oracle2 = oracles2.FirstOrDefault(o => o.A.SequenceEqual(oraclePubKey));

            // If the oracle is not present on node 1, add it
            if(oracle1 == null) {
                oracle1 = await lit1.AddOracle(oraclePubKey,"Tutorial");
            }

            // If the oracle is not present on node 2, add it
            if(oracle2 == null) {
                oracle2 = await lit2.AddOracle(oraclePubKey,"Tutorial");
            }

            // Return the index the oracle has on both nodes
            return new int[] { oracle1.Idx, oracle2.Idx };
        }

        static async Task<DlcContract> CreateContract(int oracleIdx) {
            // Create a new empty draft contract
            var contract = await lit1.NewContract();

            // Configure the contract to use the oracle we need
            await lit1.SetContractOracle(contract.Idx, oracleIdx);

            // Set the settlement time to June 13, 2018 midnight UTC
            await lit1.SetContractSettlementTime(contract.Idx, 1528848000);

            // Set the coin type of the contract to Bitcoin Regtest
            await lit1.SetContractCoinType(contract.Idx, 257);

            // Configure the contract to use the R-point we need
            await lit1.SetContractRPoint(contract.Idx, rPoint);

            // Set the contract funding to 1 BTC each
            await lit1.SetContractFunding(contract.Idx, 100000000, 100000000);

            // Configure the contract division so that we get all the
            // funds when the value is 20000, and our counter party gets
            // all the funds when the value is 10000
            await lit1.SetContractDivision(contract.Idx, 20000, 10000);

            return contract;
        }

        static async Task AcceptContract() {
            // Get all contracts for node 2
            var contracts = await lit2.ListContracts();

            // Find the first contract that's not accepted and accept it.
            foreach(var contract in contracts) {
                if(contract.Status == DlcContractStatus.ContractStatusOfferedToMe) {
                    await lit2.AcceptContract(contract.Idx);
                    return;
                }
            }
        }

        static async Task<bool> IsContractActive(int contractIdx) {
            // Fetch the contract from node 1
            var contract = await lit1.GetContract(contractIdx);

            // Return if the contract is active
            return (contract.Status == DlcContractStatus.ContractStatusActive);
        }


        private static byte[] StringToByteArray(string hex) {
            
                return Enumerable.Range(0, hex.Length)
                     .Where(x => x % 2 == 0)
                     .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                     .ToArray();
        }
    }
}
