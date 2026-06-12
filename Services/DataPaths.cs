namespace Inventario.Services;

public static class DataPaths
{
    public static string DataRoot(IWebHostEnvironment env, IConfiguration config)
    {
        var configured = config["Inventory:DataRoot"];
        var root = configured ?? (env.IsDevelopment() ? "App_Data" : "/data");
        return Path.IsPathRooted(root) ? root : Path.Combine(env.ContentRootPath, root);
    }

    public static string DatabasePath(IWebHostEnvironment env, IConfiguration config)
        => Path.Combine(DataRoot(env, config), "inventario.sqlite");

    public static string UploadRoot(IWebHostEnvironment env, IConfiguration config)
        => Path.Combine(DataRoot(env, config), "uploads");

    public static string ImportRoot(IWebHostEnvironment env, IConfiguration config)
        => Path.Combine(DataRoot(env, config), "imports");
}
