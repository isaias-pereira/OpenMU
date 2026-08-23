$porta = 55901   # porta do GameServer
$client = New-Object System.Net.Sockets.TcpClient('127.0.0.1',$porta)
$stream = $client.GetStream()
$writer = New-Object System.IO.StreamWriter($stream)
$writer.AutoFlush = $true

# comando que será enviado
$writer.WriteLine('/f 10')   # altere para /a, /v, /e ou /c conforme desejar

$reader = New-Object System.IO.StreamReader($stream)
$resposta = $reader.ReadToEnd()   # <-- sem aspas extras

$client.Close()
Write-Host "Resposta: $resposta"