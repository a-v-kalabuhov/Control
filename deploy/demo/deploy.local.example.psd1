# Скопировать в deploy.local.psd1 (в git не хранится) и поправить пути к сертификату.
@{
    Host      = '195.208.2.44'
    User      = 'deploy'
    RemoteDir = 'wtctrl-demo'                       # относительно домашней папки пользователя на VPS
    Domain    = 'control-demo.ru'
    CertFile  = 'C:\certs\control-demo.ru\control-demo.ru.crt'   # сертификат домена
    CaFile    = 'C:\certs\control-demo.ru\ca.crt'                # корневой/промежуточный от УЦ
    KeyFile   = 'C:\certs\control-demo.ru\control-demo.ru.key'   # закрытый ключ (без пароля)
}
