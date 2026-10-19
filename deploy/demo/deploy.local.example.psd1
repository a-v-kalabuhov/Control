# Скопировать в deploy.local.psd1 (в git не хранится) и поправить пути к сертификату.
@{
    Host      = '195.208.2.44'
    User      = 'deploy'
    RemoteDir = 'wtctrl-demo'                       # относительно домашней папки пользователя на VPS
    Domain    = 'controldemo.ru'
    CertFile  = 'C:\certs\controldemo.ru\controldemo.ru.crt'   # сертификат домена
    CaFile    = 'C:\certs\controldemo.ru\ca.crt'                # корневой/промежуточный от УЦ
    KeyFile   = 'C:\certs\controldemo.ru\controldemo.ru.key'   # закрытый ключ (без пароля)
}
