using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PrintAgent.Services;

public class PrinterDiscoveryService
{
    private const byte PrinterHostOctet = 110;
    /// <summary>
    /// Obtener la IP activa del anfitrion, en este caso equipo de capacitación Fedora
    /// </summary>
    /// 
    
    public IPAddress? GetLocalSubnetIP()
    {
        //Método para obtener todas las interfaces de red del equipo
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        foreach(var netInterface in interfaces)
        {
            //Identificamos si la intefraz esta apagada o de loopback(127.0.0.1)
            if (netInterface.OperationalStatus != OperationalStatus.Up || netInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            //Pbtener las propiedades de la IP de esa interfaz
            var ipProperties = netInterface.GetIPProperties();
            foreach(var ip in ipProperties.UnicastAddresses)
            {
                var address = ip.Address;
                //Solo buscamos direcciones IPv4
                if (address.AddressFamily == AddressFamily.InterNetwork) // => AddressFamily.InterNetwork es para obtener direcciones IPv4 
                {
                    byte[] bytes = address.GetAddressBytes();
                    //verificamos que la dirección IP sea de la subred 10.x.x.x
                    if (bytes[0] == 10)
                    {
                        //Si es de la subred 10.x.x.x, devolvemos la dirección IP
                        return address;
                    }
                }
            }
        }
        return null;
    }
    /// <summary>
    /// Calcular la IP de la impresora a partir de la IP del equipo de capacitación 
    /// </summary>
    public string? ResolvePrinterIP()
    {
        var localIP = GetLocalSubnetIP();
        if (localIP == null) return null;

        //Extraemos los 4 octetos de la dirección IP 
        byte[] bytes = localIP.GetAddressBytes();
        //Calculamos la IP de la impresora
        bytes[3] = PrinterHostOctet;
        return new IPAddress(bytes).ToString();
    }
}
