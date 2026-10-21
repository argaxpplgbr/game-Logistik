using MySqlConnector;

namespace Nusantara.Database;

public static class Db
{
    // UNTUK NAMA DATABASE ITU MENYESUAIKAN BESERTA PASSWORD NYA JUGA KALO GAK ADA DAN BEDA BERARTI BISA KEMUNGKINAN EROR
    public const string ConnStr =
        "Server=localhost;Port=3306;Database=nusantara_logistics;User=root;Password=GANTI_PASSWORD_KM_ATAU_DIEMIN;";

    public static MySqlConnection Open()
    {
        var c = new MySqlConnection(ConnStr);
        c.Open();
        return c;
    }
}
