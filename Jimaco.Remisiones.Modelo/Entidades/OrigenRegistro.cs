namespace Jimaco.Remisiones.Modelo.Entidades;

// De dónde viene un producto o un comprador: cargado desde World Office (Fase 2: sincronización)
// o creado a mano en esta app. Los de origen Local son los que habrá que empujar a WO.
public enum OrigenRegistro
{
    Local = 1,
    WorldOffice = 2
}
