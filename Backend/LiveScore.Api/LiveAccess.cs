using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace LiveScore.Api;
public static class LiveAccess
{
    public static bool LocalAdmin(HttpContext context)
    {
        var ip=context.Connection.RemoteIpAddress;var host=context.Request.Host.Host;if(ip?.IsIPv4MappedToIPv6==true)ip=ip.MapToIPv4();
        return ip is not null&&IPAddress.IsLoopback(ip)&&(host.Equals("localhost",StringComparison.OrdinalIgnoreCase)||IPAddress.TryParse(host,out var address)&&IPAddress.IsLoopback(address));
    }
    public static bool VenuePeer(IPAddress? ip)
    {
        if(ip is null)return false;if(ip.IsIPv4MappedToIPv6)ip=ip.MapToIPv4();if(IPAddress.IsLoopback(ip))return true;
        if(ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork){var b=ip.GetAddressBytes();return b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1]>=16&&b[1]<=31;}
        return ip.IsIPv6LinkLocal||(ip.GetAddressBytes()[0]&0xfe)==0xfc;
    }
    public static string Peer(HttpContext c)=>c.Connection.RemoteIpAddress?.IsIPv4MappedToIPv6==true?c.Connection.RemoteIpAddress.MapToIPv4().ToString():c.Connection.RemoteIpAddress?.ToString()??"";
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool Matches(string hash,string value)=>hash.Length==64&&value.Length<=256&&CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash),SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Token()=>Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+','-').Replace('/','_');
    public static string Code()=>string.Concat(RandomNumberGenerator.GetBytes(8).Select(b=>"ABCDEFGHJKLMNPQRSTUVWXYZ23456789"[b%32]));
    public static string Bearer(HttpContext c)
    {
        var header=c.Request.Headers.Authorization.ToString();if(!header.StartsWith("Bearer ",StringComparison.Ordinal)||header.Length>263)throw new RuleException("Tablet session is missing or closed. Ask the organiser for a new access code.",401);
        return header[7..];
    }
}
