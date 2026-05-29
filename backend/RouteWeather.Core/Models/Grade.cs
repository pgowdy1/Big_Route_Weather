namespace RouteWeather.Core.Models;

public enum Grade
{
    A,
    B,
    C,
    D,
    F
}

public static class GradeMapping
{
    public static Grade FromScore(int score) => score switch
    {
        >= 90 => Grade.A,
        >= 80 => Grade.B,
        >= 70 => Grade.C,
        >= 60 => Grade.D,
        _ => Grade.F,
    };
}
