using System.Globalization;
using System.Data;
using System.Data.Common;
using System.Diagnostics.Tracing;
using MySql.Data.MySqlClient;

namespace botiguers;

class Program
{
    public static Comandes comandes = new();
    public  static Dictionary<long, decimal> goodproducts { get; set; } = new();
    static void Main() {
        using var con = DbConnexion();
        if (con == null || con.State != ConnectionState.Open)
        {
            Console.WriteLine("Unable to establish connection to the database");
            return;
        }
        var productes = DataSet("SELECT * FROM productes WHERE categoria_id = 1", "productes" ,con);
        ListGoodProd(productes);
        var rebuts = DataSet("SELECT * FROM rebuts", "rebuts" ,con);
        var rebut_items  = DataSet("SELECT * FROM rebut_items", "rebut_items" ,con);
        
        DemanarIdComandes(rebuts);
        foreach (var comanda in comandes.id)
        {
            comandes.productesComanda.Clear();
            comandes.cambiar.Clear();
            DemanarProductesComanda(rebut_items,comanda);
            FiltrarProductesComanda(con);
        }
    }
    private static MySqlConnection? DbConnexion()
    {
        try {
            var con = new MySqlConnection("Server=127.0.0.1;port=3306;userid=root;password=password;database=vendes;");
            con.Open();
            return con;
        }
        catch (Exception ex) {
            Console.WriteLine("Database connection error: " + ex.Message);
            return null;
        }
    }
    private static DataSet DataSet(string query, string nomTaula ,MySqlConnection con)
    {
        DataSet dataset = new();
        try {
            dataset.Tables.Add(nomTaula);
            MySqlDataAdapter adapter = new(query, con);
            adapter.Fill(dataset, nomTaula);
        }
        catch (Exception ex) {
            Console.WriteLine($"ERROR {nomTaula}: {ex.Message}");
        }
        return dataset; 
    }
    private static void ListGoodProd(DataSet products)
    {
        try
        {
            if (!products.Tables.Contains("productes"))
                throw new Exception("La taula 'productes' no existeix al DataSet.");
            
            DataRow[] good = products.Tables["productes"].Select("categoria_id = 1");
            foreach (DataRow product in good)
            {
                var id = Convert.ToInt64(product["id"]);
                var preuP = Convert.ToDecimal(product["preu"]);
                goodproducts.Add(id, preuP);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR {products}: {ex.Message}");
        }
    }
    private static void DemanarIdComandes(DataSet rebuts)
    {
        foreach (DataRow table in rebuts.Tables[0].Rows)
        {
            comandes.id.Add(Convert.ToInt64(table["id"]));
        }
    }
    private static void DemanarProductesComanda(DataSet rebutItems, long id)
    {
        DataRow[] infoRebut = rebutItems.Tables["rebut_items"].Select($"rebut_id = {id}");
        foreach (DataRow producte in infoRebut) {
            long producteId = Convert.ToInt64(producte["producte_id"]);
            int quantitat = Convert.ToInt32(producte["quantity"]);
            decimal preu = Convert.ToDecimal(producte["preu"]);
            ProductesComanda productesComanda = new()
            {
                rebut_id = id,
                producte_id = producteId,
                quantity = quantitat,
                preu = preu
            };
            comandes.productesComanda.Add(productesComanda);
        }
    }

    private static void FiltrarProductesComanda(MySqlConnection con)
    {
        foreach (var producte in comandes.productesComanda)
        {
            if (!goodproducts.ContainsKey(producte.producte_id))
            {
                ProductesACambiar pendent = new ProductesACambiar
                {
                    producte_id = producte.producte_id,
                    rebut_id = producte.rebut_id,
                    quantity = producte.quantity,
                    preu = producte.preu
                };
                comandes.cambiar.Add(pendent);
                SubstituirProducte(con, producte.rebut_id);
            }
        }
    }

    private static bool IntentarSubstituir(MySqlConnection con, long rebutId, long prodOriginal, long prodNou, decimal preu, int quantity)
    {
        try
        {
            string updateQuery = @" UPDATE rebut_items SET producte_id = @nouId, quantity = @quantitat WHERE rebut_id = @rebutId AND producte_id = @vellId AND preu = @preu";

            using var cmd = new MySqlCommand(updateQuery, con);
            cmd.Parameters.AddWithValue("@nouId", prodNou);
            cmd.Parameters.AddWithValue("@quantitat", quantity);
            cmd.Parameters.AddWithValue("@rebutId", rebutId);
            cmd.Parameters.AddWithValue("@vellId", prodOriginal);
            cmd.Parameters.AddWithValue("@preu", preu);
        
            int result = cmd.ExecuteNonQuery();
            return result > 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR substituint producte {prodOriginal} -> {prodNou}: {ex.Message}");
            return false;
        }
    }

    private static void SubstituirProducte(MySqlConnection connection, long rebutId)
    {
        try
        {
            foreach (var producte in comandes.cambiar) {
                bool canvi = false;
                var barats = goodproducts.Where(x => x.Value <= producte.preu).ToList();
                var combinacio = TrobarCombinacions(barats, producte.preu);
                if (combinacio != null && combinacio.Any()) {
                    foreach (var producteCombinacio in combinacio)
                    {
                        canvi = IntentarSubstituir(connection, producte.rebut_id, producte.producte_id, producteCombinacio.Key, producte.preu, producte.quantity);
                        if (canvi) break;
                    }
                }
                else {
                    foreach (var good in goodproducts)
                    {
                        if (good.Value == producte.preu)
                        {
                            canvi = IntentarSubstituir(connection, producte.rebut_id, producte.producte_id, good.Key, producte.preu, producte.quantity);
                            if (canvi) break;
                        }
                    }
                }
                if (!canvi) {
                    Console.WriteLine("No s'ha trobat un porducte per substituir el producte dolent");
                }
            }
            ImprimirComandaFinal(connection, rebutId);
        }
        catch (Exception ex) {
            Console.WriteLine($"ERROR en el procés de substitució: {ex.Message}");
        }
    }

    private static List<KeyValuePair<long, decimal>>? TrobarCombinacions(List<KeyValuePair<long, decimal>> productos, decimal preuObjectiu)
    {
        try
        {
            List<KeyValuePair<long, decimal>> combinacio = new();
            decimal suma = 0;
            var productesSeleccionats = new List<KeyValuePair<long, decimal>>();

            foreach (var producte in productos.OrderBy(x => x.Value))
            {
                suma += producte.Value;
                productesSeleccionats.Add(producte);

                if (suma >= preuObjectiu)
                {
                    // Si la suma es mayor, descartamos la combinación
                    if (suma == preuObjectiu)
                    {
                        combinacio = new List<KeyValuePair<long, decimal>>(productesSeleccionats);
                    }
                    break;
                }
            }
            return combinacio.Any() ? combinacio : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR al intentar buscar un combinació de productes: {ex.Message}");
            return null;
        }
    }

    private static void ImprimirComandaFinal(MySqlConnection con, long rebutId)
    {
        string query = $@" SELECT r.rebut_id, p.nom AS nom_producte, r.preu FROM rebut_items r JOIN productes p ON r.producte_id = p.id WHERE r.rebut_id = {rebutId}";

        try
        {
            using var cmd = new MySqlCommand(query, con);
            cmd.Parameters.AddWithValue("@rebutId", rebutId);
            using var reader = cmd.ExecuteReader();

            Console.WriteLine($"\nComanda amb ID {rebutId}:");
            while (reader.Read())
            {
                Console.WriteLine(
                    $" - Producte: {reader["nom_producte"]}, Preu: {Convert.ToDecimal(reader["preu"]):0.00}€");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR imprimint comanda {rebutId}: {ex.Message}");
        }
    }
}