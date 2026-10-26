using System.Text.Json;

namespace Nusantara.Services;

public record NewsDraft(string Category, string Headline, string Body);

// Narator berbasis template. Semua angka dan nama diambil dari payload kejadian.
// Variasi kalimat dipilih deterministik dari event_id, jadi hasilnya selalu sama untuk kejadian yang sama.
public static class NewsComposer
{
    public const int TemplateVersion = 1;

    public static string Str(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";

    public static long Num(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    public static bool Flag(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;

    static string Pick(long id, params string[] variants) => variants[(int)(Math.Abs(id) % variants.Length)];

    // null = kejadian ini tidak dijadikan berita.
    public static NewsDraft? Compose(string type, JsonElement e, long id)
    {
        switch (type)
        {
            case "COMPANY_FOUNDED":
                return new NewsDraft("PASAR",
                    $"{Str(e, "company")} resmi berdiri",
                    $"Perusahaan logistik baru memulai usaha dengan modal awal {Rules.Rp(Num(e, "cash"))}.");

            case "TRUCK_BOUGHT":
                bool used = Flag(e, "used");
                return new NewsDraft("ARMADA",
                    Pick(id, $"Armada {Str(e, "company")} bertambah satu unit", $"{Str(e, "company")} menambah {Str(e, "model")} ke armadanya"),
                    $"{Str(e, "model")} ({(used ? "bekas" : "baru")}) dibeli seharga {Rules.Rp(Num(e, "price"))}.");

            case "BODY_INSTALL_STARTED":
                return new NewsDraft("ARMADA",
                    $"Karoseri {Str(e, "body")} mulai dipasang pada {Str(e, "model")}",
                    $"Biaya {Rules.Rp(Num(e, "cost"))}, pengerjaan sekitar {Num(e, "days")} hari game.");

            case "DRIVER_HIRED":
                return new NewsDraft("SDM",
                    $"{Str(e, "name")} bergabung sebagai supir",
                    $"Supir ber-SIM {Str(e, "sim")} diterima dengan gaji pokok {Rules.Rp(Num(e, "salary"))} per bulan.");

            case "DRIVER_FIRED":
                return new NewsDraft("SDM",
                    $"{Str(e, "name")} diberhentikan dari perusahaan",
                    "Supir tersebut tidak lagi bertugas dan dilepas dari truknya.");

            case "CONTRACT_ACCEPTED":
                return new NewsDraft("PASAR",
                    $"Kontrak borongan {Str(e, "client")} diterima",
                    $"Rute {Str(e, "origin")} - {Str(e, "dest")}, {Num(e, "months")} bulan, bayaran {Rules.Rp(Num(e, "monthlyPay"))} per bulan.");

            case "TRIP_DISPATCHED":
                return new NewsDraft("OPERASI",
                    Pick(id, $"{Str(e, "truck")} berangkat ke {Str(e, "dest")}", $"Pengiriman {Str(e, "origin")} - {Str(e, "dest")} dimulai"),
                    $"Trip #{Num(e, "tripId")} ({(Str(e, "kind") == "SPOT" ? "spot charter" : "kontrak borongan")}) sedang berjalan.");

            case "TRIP_COMPLETED":
                string route = $"{Str(e, "origin")} - {Str(e, "dest")}";
                if (Flag(e, "ok"))
                    return new NewsDraft("OPERASI",
                        Pick(id, $"Pengiriman {route} selesai", $"{Str(e, "truck")} tiba di {Str(e, "dest")}"),
                        $"Trip #{Num(e, "tripId")}: pendapatan {Rules.Rp(Num(e, "income"))}, laba/rugi trip {Accounting.Signed(Num(e, "net"))}."
                        + (Flag(e, "incident") ? " Ada insiden ringan di perjalanan." : ""));
                string cause = Flag(e, "total") ? "truk mengalami total loss"
                             : Flag(e, "stolen") ? "kargo hilang dicuri"
                             : "pengiriman tidak berhasil";
                return new NewsDraft("OPERASI",
                    $"Pengiriman {route} gagal",
                    $"Trip #{Num(e, "tripId")}: {cause}. Laba/rugi trip {Accounting.Signed(Num(e, "net"))}.");

            case "MONTH_CLOSED":
                if (Flag(e, "bankrupt"))
                    return new NewsDraft("KEUANGAN", "Perusahaan dinyatakan bangkrut",
                        $"Tutup buku hari {Num(e, "closeDay")} berakhir dengan kas {Rules.Rp(Num(e, "cash"))}.");
                return new NewsDraft("KEUANGAN",
                    "Tutup buku bulanan selesai",
                    $"Kas akhir {Rules.Rp(Num(e, "cash"))}." + (Flag(e, "payrollPaid") ? "" : " Gaji tertunda dan menjadi tunggakan."));

            case "LOAN_TAKEN":
                return new NewsDraft("KEUANGAN",
                    $"Pinjaman bank {Rules.Rp(Num(e, "amount"))} cair",
                    $"Tenor {Num(e, "months")} bulan, cicilan {Rules.Rp(Num(e, "installment"))} per bulan.");

            case "TRUCK_SEIZED":
                return new NewsDraft("KEUANGAN",
                    $"Bank menyita {Str(e, "model")}",
                    $"Cicilan gagal dibayar; nilai sitaan mengurangi utang sebesar {Rules.Rp(Num(e, "amount"))}.");

            case "TIER_UPGRADED":
                return new NewsDraft("PASAR",
                    $"{Str(e, "company")} naik status menjadi {Str(e, "legal")}",
                    "Izin usaha yang lebih tinggi membuka truk, rute, dan kontrak baru.");

            case "WORLD_EVENT":
                return new NewsDraft("DUNIA",
                    Str(e, "title"),
                    $"Kejadian simulasi, berlaku hari {Num(e, "startDay")} sampai {Num(e, "endDay")}.");

            case "CONTRACT_AWARDED":
                return new NewsDraft("PASAR",
                    $"{Str(e, "company")} memenangkan kontrak {Str(e, "origin")} - {Str(e, "dest")}",
                    $"Klien {Str(e, "client")}, harga {Rules.Rp(Num(e, "price"))}, {Num(e, "bidders")} penawar. {Str(e, "reason")}");

            default:
                return null;   // TRUCK_SERVICED dan tipe lain: hanya tercatat di buku kejadian
        }
    }
}