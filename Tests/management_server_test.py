"""Real Kestrel/SQLite integration: panel authentication + existing installation contracts."""
import base64, concurrent.futures, contextlib, hashlib, hmac, http.cookies, json, os, pathlib, secrets, socket, sqlite3, struct, subprocess, tempfile, time, urllib.request, urllib.error
ROOT=pathlib.Path(__file__).resolve().parents[1]
DLL=ROOT/'Server/bin/Debug/net10.0/CaixaLume.Management.dll'
def totp(secret,step=None):
 key=base64.b32decode(secret); step=int(time.time())//30 if step is None else step
 digest=hmac.new(key,struct.pack('>Q',step),hashlib.sha1).digest(); offset=digest[-1]&15
 return str((int.from_bytes(digest[offset:offset+4],'big')&0x7fffffff)%1000000).zfill(6)
def main():
 with tempfile.TemporaryDirectory(prefix='CaixaLumeServer-') as temp:
  db=str(pathlib.Path(temp)/'management.db'); admin=secrets.token_hex(32)
  with socket.socket() as sock: sock.bind(('127.0.0.1',0)); port=sock.getsockname()[1]
  base=f'http://127.0.0.1:{port}'
  env=dict(os.environ,CAIXALUME_ADMIN_TOKEN=admin,CAIXALUME_MANAGEMENT_DB=db,ASPNETCORE_ENVIRONMENT='Development',ASPNETCORE_URLS=base)
  env['Logging__LogLevel__Default']='Warning'
  process=None; cookie=''; csrf=''; passed=0; password='Uma senha exclusiva de teste 123!'
  def start():
   nonlocal process
   process=subprocess.Popen(['dotnet',str(DLL)],cwd=ROOT/'Server',env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
   for _ in range(100):
    try:
     with socket.create_connection(('127.0.0.1',port),timeout=.1): return
    except OSError:
     if process.poll() is not None: raise RuntimeError('Server stopped during startup')
     time.sleep(.1)
   raise RuntimeError('Server did not start')
  def stop():
   if process and process.poll() is None: process.terminate();process.wait(timeout=10)
  def sql(statement,args=()):
   with contextlib.closing(sqlite3.connect(db)) as c:
    result=c.execute(statement,args).fetchall();c.commit();return result
  def request(path,method='GET',data=None,token=None,host=None,auth=False,csrf_value=None,origin=None,custom=True,cookie_value=None):
   nonlocal cookie
   headers={'Content-Type':'application/json'}
   if token: headers['Authorization']='Bearer '+token
   if host: headers['Host']=host
   if origin: headers['Origin']=origin
   if method!='GET' and custom: headers['X-CaixaLume-Request']='1'
   if auth: headers['Cookie']=cookie if cookie_value is None else cookie_value
   if auth and method!='GET': headers['X-CSRF-Token']=csrf if csrf_value is None else csrf_value
   req=urllib.request.Request(base+path,data=json.dumps(data).encode() if data is not None else None,headers=headers,method=method)
   try:
    with urllib.request.urlopen(req,timeout=15) as r:
     if r.headers.get('Set-Cookie'): cookie=r.headers['Set-Cookie'].split(';',1)[0]
     return r.status,json.loads(r.read() or b'null'),dict(r.headers)
   except urllib.error.HTTPError as e: return e.code,None,dict(e.headers)
  def check(condition,name):
   nonlocal passed
   assert condition,name
   passed+=1;print(f'PASSOU servidor {passed:02}: {name}')
  def refresh():
   nonlocal csrf
   result=request('/auth/session',auth=True);assert result[0]==200;csrf=result[1]['csrf'];return result
  def login(code,remember=False,passwd=None):
   return request('/auth/login','POST',{'login':'gabryelle','password':password if passwd is None else passwd,'code':code,'remember':remember})
  try:
   start()
   # Simulate a pre-authentication production database with existing customers and devices.
   sql("INSERT INTO Stores(Id,Name) VALUES('legacy-store','Existing market')")
   sql("INSERT INTO Devices(Id,StoreId,SecretHash) VALUES('legacy-device','legacy-store',?)",(hashlib.sha256(b'legacy-token').hexdigest().upper(),))
   check(request('/auth/status')[1]=={'configured':False},'Initial setup state detected without exposing key')
   check(request('/admin/stores',token=admin)[0]==401,'Old administrative key no longer authorizes panel operations')
   check(request('/auth/setup/start','POST',{'bootstrapToken':'wrong','login':'gabryelle','password':password})[0]==401,'Initial setup requires valid existing bootstrap key')
   check(request('/auth/setup/start','POST',{'bootstrapToken':admin,'login':'gabryelle','password':'weak'})[0]==400,'Weak owner password rejected')
   setup=request('/auth/setup/start','POST',{'bootstrapToken':admin,'login':'gabryelle','password':password})[1]; secret=setup['secret']
   check(bool(secret) and setup['uri'].startswith('otpauth://totp/'),'Authenticator setup returns a standard TOTP enrollment')
   wrong=str((int(totp(secret))+123)%1000000).zfill(6)
   check(request('/auth/setup/finish','POST',{'challenge':setup['challenge'],'code':wrong})[0]==401 and request('/auth/status')[1]['configured'] is False,'Incorrect TOTP cannot finish setup')
   result=request('/auth/setup/finish','POST',{'challenge':setup['challenge'],'code':totp(secret)})
   recovery=result[1]['recoveryCodes']; refresh()
   check(result[0]==200 and len(recovery)==10,'Account created only after confirmed MFA, recovery codes issued once')
   check('httponly' in result[2]['Set-Cookie'].lower() and 'samesite=strict' in result[2]['Set-Cookie'].lower() and 'expires=' in result[2]['Set-Cookie'].lower(),'Session cookie is HttpOnly, SameSite and persistent with expiry')
   stored=sql('SELECT PasswordHash,Totp FROM PanelAccount')[0]
   check(password not in stored[0] and stored[1]!=secret and len(sql('SELECT * FROM PanelRecovery'))==10,'Password derived and authenticator protected at rest')
   check(request('/auth/setup/start','POST',{'bootstrapToken':admin,'login':'other','password':password})[0]==401,'Bootstrap key cannot recreate or bypass configured owner')
   check(request('/auth/setup/finish','POST',{'challenge':setup['challenge'],'code':totp(secret)})[0]==401,'Setup challenge cannot be replayed')
   check(request('/admin/stores',auth=True)[1][0]['installationId']=='legacy-device','Existing markets and devices preserved by additive auth tables')
   check(request('/admin/stores',auth=True,host='public.example')[0]==403,'Remote administrative host remains blocked')
   check(request('/admin/stores','POST',{'name':'forbidden'},auth=True,csrf_value='wrong')[0]==403,'Forged CSRF rejected')
   check(request('/admin/stores','POST',{'name':'forbidden'},auth=True,origin='https://attacker.example')[0]==403,'Cross-origin writes rejected')
   check(request('/auth/logout','POST',{},auth=True,custom=False)[0]==403,'Writes without custom same-origin request header rejected')
   old_cookie=cookie
   check(request('/auth/logout','POST',{},auth=True)[0]==204 and request('/auth/session',auth=True,cookie_value=old_cookie)[0]==401,'Logout revokes server-side session')
   check(login('000000',passwd='wrong password')[0]==401,'Incorrect password does not create session')
   check(login(totp(secret))[0]==401,'TOTP already used at enrollment cannot be replayed')
   code=recovery.pop()
   check(login(code,remember=True)[0]==200,'Recovery code plus password restores access')
   refresh();remember_expiry=sql('SELECT MAX(Expires) FROM PanelSessions')[0][0]
   check(remember_expiry-int(time.time())>6*86400,'Remember-device session has a bounded seven-day expiry')
   check(login(code)[0]==401,'Recovery code is consumed only once')
   refresh();cookie_before=cookie
   stop();env.pop('CAIXALUME_ADMIN_TOKEN',None);start();check(request('/auth/session',auth=True)[0]==200 and request('/admin/stores',auth=True)[0]==200,'Session survives restart and old bootstrap key is no longer required')
   refresh()
   # A second session, then revoke both.
   check(login(recovery.pop())[0]==200,'Another legitimate session can be opened')
   refresh();check(request('/auth/security/logout-all','POST',{},auth=True)[0]==204 and request('/auth/session',auth=True,cookie_value=cookie_before)[0]==401,'Logout-all revokes every prior session')
   check(login(recovery.pop())[0]==200,'Login remains available after session revocation');refresh()
   old_cookie=cookie
   new_password='Outra senha exclusiva para o painel 456!'
   check(request('/auth/security/password','POST',{'currentPassword':password,'newPassword':new_password,'code':recovery.pop()},auth=True)[0]==200,'Password change requires password and a second factor')
   password=new_password;refresh()
   check(request('/auth/session',auth=True,cookie_value=old_cookie)[0]==401,'Password change revokes previous sessions')
   reset=request('/auth/security/totp/start','POST',{'password':password},auth=True)[1]
   reset_result=request('/auth/security/totp/finish','POST',{'challenge':reset['challenge'],'code':totp(reset['secret'])},auth=True)
   old_recovery=recovery[:];recovery=reset_result[1]['recoveryCodes'];refresh();secret=reset['secret']
   check(reset_result[0]==200 and login(old_recovery.pop())[0]==401,'Authenticator reset replaces old recovery codes');refresh()
   check(request('/auth/security/password','POST',{'currentPassword':password,'newPassword':password,'code':'000000'})[0]==401,'Security settings reject requests without a session')
   # A valid six-digit code can be accepted only once, including concurrent requests.
   sql('UPDATE PanelAccount SET LastStep=-1,Failures=0,LockedUntil=0')
   one_code=totp(secret)
   with concurrent.futures.ThreadPoolExecutor(2) as pool: results=list(pool.map(lambda _:login(one_code)[0],range(2)))
   check(sorted(results)==[200,401],'Concurrent MFA replay creates only one session')
   refresh()
   session_token=cookie.split('=',1)[1]
   check(sql('SELECT COUNT(*) FROM PanelSessions WHERE Hash=?',(session_token,))[0][0]==0,'Raw session credential is never stored in the database')
   check(request('/admin/stores',auth=True,cookie_value=cookie.split('=',1)[0]+'='+secrets.token_hex(32))[0]==401,'Forged opaque session is rejected')
   # Lockout is persistent and does not require waiting in the test.
   sql('UPDATE PanelAccount SET Failures=0,LockedUntil=0')
   for _ in range(5): login('000000',passwd='wrong password')
   check(sql('SELECT LockedUntil FROM PanelAccount')[0][0]>int(time.time()) and login(recovery[-1])[0]==401,'Repeated failed logins temporarily block correct credentials too')
   sql('UPDATE PanelAccount SET Failures=0,LockedUntil=0')
   # Preserve the existing session for the installation contract tests below.
   refresh()
   check(request('/admin/stores','POST',{'name':' '},auth=True)[0]==400,'Invalid store rejected')
   a=request('/admin/stores','POST',{'name':'Market A'},auth=True)[1]['id'];b=request('/admin/stores','POST',{'name':'Market B'},auth=True)[1]['id']
   check(a!=b,'Markets have independent identities')
   code=request(f'/admin/stores/{a}/invites','POST',{},auth=True)[1]['code']
   status,device,_=request('/api/enroll','POST',{'code':code})
   check(status==200 and device['establishmentId']==a,'Enrollment bound to invited market')
   check(request('/api/enroll','POST',{'code':code})[0]==401,'Invite cannot be reused')
   code2=request(f'/admin/stores/{b}/invites','POST',{},auth=True)[1]['code']
   with concurrent.futures.ThreadPoolExecutor(2) as pool: statuses=list(pool.map(lambda _:request('/api/enroll','POST',{'code':code2})[0],range(2)))
   check(sorted(statuses)==[200,401],'Concurrent enrollment consumes invite once')
   check(request('/api/heartbeat','POST',{'version':'0.1.3','schemaVersion':8},token=admin)[0]==401,'Bootstrap credential is not a device credential')
   check(request('/api/heartbeat','POST',{'version':'0.1.3','schemaVersion':8},token=device['token'])[0]==204,'Authenticated heartbeat contract preserved')
   check(request('/api/heartbeat','POST',{'version':'invalid','schemaVersion':8},token=device['token'])[0]==401,'Invalid metadata rejected')
   check(request('/admin/stores',token=device['token'])[0]==401,'Device cannot access panel administration')
   rows=request('/admin/stores',auth=True)[1]
   check(device['token'] not in json.dumps(rows) and next(r for r in rows if r['installationId']==device['installationId'])['version']=='0.1.3','Panel metadata does not expose credentials')
   check(sql('SELECT SecretHash FROM Devices WHERE Id=?',(device['installationId'],))[0][0]!=device['token'],'Device credential remains hashed')
   expired=request(f'/admin/stores/{a}/invites','POST',{},auth=True)[1]['code']
   sql("UPDATE Invites SET Expires='2000-01-01' WHERE Hash=?",(hashlib.sha256(expired.encode()).hexdigest().upper(),))
   check(request('/api/enroll','POST',{'code':expired})[0]==401,'Expired invite rejected')
   stop();start()
   check(request('/api/heartbeat','POST',{'version':'0.1.3','schemaVersion':8},token=device['token'])[0]==204,'Installation credential survives server restart')
   check(request(f"/admin/installations/{device['installationId']}/revoke",'POST',{},auth=True)[0]==204 and request('/api/heartbeat','POST',{'version':'0.1.3','schemaVersion':8},token=device['token'])[0]==401,'Revocation blocks device communication')
   check(request('/admin/stores/missing/invites','POST',{},auth=True)[0]==404,'Unknown market rejected')
   check(request('/api/enroll','POST',{'code':'A'*5000})[0]==413,'Oversized payload rejected')
   sql('UPDATE PanelSessions SET Expires=1')
   check(request('/admin/stores',auth=True)[0]==401,'Expired browser session is rejected server-side')
   codes=[request('/health')[0] for _ in range(125)];check(429 in codes,'Global request limit remains active')
   stop();env['ASPNETCORE_ENVIRONMENT']='Production';start();check(request('/health')[0]==400,'Production refuses plaintext HTTP')
   stop()
   import datetime, ssl
   from cryptography import x509
   from cryptography.hazmat.primitives import hashes, serialization
   from cryptography.hazmat.primitives.asymmetric import rsa
   from cryptography.x509.oid import NameOID
   key=rsa.generate_private_key(public_exponent=65537,key_size=2048)
   subject=x509.Name([x509.NameAttribute(NameOID.COMMON_NAME,'localhost')])
   now=datetime.datetime.now(datetime.timezone.utc)
   cert=x509.CertificateBuilder().subject_name(subject).issuer_name(subject).public_key(key.public_key()).serial_number(x509.random_serial_number()).not_valid_before(now-datetime.timedelta(minutes=1)).not_valid_after(now+datetime.timedelta(days=1)).add_extension(x509.SubjectAlternativeName([x509.DNSName('localhost')]),critical=False).sign(key,hashes.SHA256())
   cert_path=pathlib.Path(temp)/'test.crt';key_path=pathlib.Path(temp)/'test.key'
   cert_path.write_bytes(cert.public_bytes(serialization.Encoding.PEM));key_path.write_bytes(key.private_bytes(serialization.Encoding.PEM,serialization.PrivateFormat.PKCS8,serialization.NoEncryption()))
   env['ASPNETCORE_URLS']=f'https://127.0.0.1:{port}'
   env['ASPNETCORE_Kestrel__Certificates__Default__Path']=str(cert_path)
   env['ASPNETCORE_Kestrel__Certificates__Default__KeyPath']=str(key_path)
   sql('UPDATE PanelAccount SET Failures=0,LockedUntil=0')
   start()
   ssl_context=ssl.create_default_context(cafile=str(cert_path))
   req=urllib.request.Request(f'https://localhost:{port}/auth/login',data=json.dumps({'login':'gabryelle','password':password,'code':recovery.pop(),'remember':True}).encode(),headers={'Content-Type':'application/json','X-CaixaLume-Request':'1'},method='POST')
   with urllib.request.urlopen(req,context=ssl_context,timeout=15) as r:
    check(r.status==200 and 'secure' in r.headers['Set-Cookie'].lower(),'HTTPS login verifies TLS and sets Secure cookie')
   print(f'SUCESSO: {passed} cenários de servidor e autenticação.')
  finally: stop()
if __name__=='__main__': main()
