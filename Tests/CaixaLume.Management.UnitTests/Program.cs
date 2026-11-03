using System.Text;
var secret=PanelAuth.Base32(Encoding.ASCII.GetBytes("12345678901234567890"));
(long Time,string Code)[] vectors=[(59,"287082"),(1111111109,"081804"),(1111111111,"050471"),(1234567890,"005924"),(2000000000,"279037"),(20000000000,"353130")];
foreach(var vector in vectors){if(PanelAuth.Totp(secret,vector.Time/30)!=vector.Code)throw new Exception("Falha no vetor RFC 6238");Console.WriteLine($"PASSOU RFC 6238: {vector.Time}");}
Console.WriteLine("SUCESSO: 6 vetores oficiais TOTP (SHA-1, seis dígitos).");
