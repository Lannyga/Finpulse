// Independent HTTPS validation of the real portal; no external packages or database DDL.
const fs=require('node:fs'), path=require('node:path'), https=require('node:https');
const net=require('node:net'), crypto=require('node:crypto'), {spawn,spawnSync}=require('node:child_process');
const {once}=require('node:events');
const root=path.resolve(__dirname,'..');
const qa=path.join(root,'tests','.qa','oracle-http-'+crypto.randomUUID()); fs.mkdirSync(qa,{recursive:true});
const web=path.join(root,'src','Finpulse.Web','bin','Release','net10.0','Finpulse.Web.dll');
const env={...process.env,ASPNETCORE_ENVIRONMENT:'Development',Database__Provider:'Oracle',Oracle__ExpectedUser:'FINPULSE_WEB_TEST',DataProtection__KeyDirectory:path.join(qa,'keys')};
if(!env.ConnectionStrings__Oracle) throw Error('Se requiere la conexion aislada mediante entorno local.');
const certificate=process.env.FINPULSE_QA_CERTIFICATE;
if(!certificate||!fs.existsSync(certificate)) throw Error('Genera el certificado local mediante scripts/validar-oracle-http.ps1.');
env.Kestrel__Certificates__Default__Path=certificate;
let server,port,count=0;
function check(ok,label){if(!ok)throw Error(label); console.log(`HTTP ORACLE ${++count}: ${label}`);}
function request(client,route,fields){
  const payload=fields===undefined?null:new URLSearchParams(fields).toString();
  return new Promise((resolve,reject)=>{
    const headers={Cookie:[...client.entries()].map(([k,v])=>`${k}=${v}`).join(';')};
    if(payload!==null){headers['Content-Type']='application/x-www-form-urlencoded';headers['Content-Length']=Buffer.byteLength(payload);}
    const r=https.request({hostname:'localhost',port,path:route,method:payload===null?'GET':'POST',rejectUnauthorized:false,headers},res=>{
      for(const raw of res.headers['set-cookie']||[]){const pair=raw.split(';',1)[0],i=pair.indexOf('=');client.set(pair.slice(0,i),pair.slice(i+1));}
      let body='';res.setEncoding('utf8');res.on('data',c=>body+=c);res.on('end',()=>resolve({status:res.statusCode,body,headers:res.headers}));
    });r.on('error',reject);r.setTimeout(15000,()=>r.destroy(Error('Tiempo de espera HTTP.')));if(payload!==null)r.write(payload);r.end();
  });
}
async function get(c,url){const r=await request(c,url);check(r.status===200,'GET '+url);return r.body;}
const field=(html,name)=>html.match(new RegExp('name="'+name+'"[^>]*value="([^"]*)"'))?.[1]?.replaceAll('&amp;','&')||'';
const post=(c,url,page,values)=>request(c,url,{...values,__RequestVerificationToken:field(page,'__RequestVerificationToken')});
async function saved(c,url,page,values,label){const r=await post(c,url,page,values);if(r.status!==302)fs.writeFileSync(path.join(qa,'last-response.html'),r.body);check(r.status===302,label);}
async function stop(){if(server&&!server.killed){const end=once(server,'exit');server.kill();await end;}server=null;}
async function start(){
  env.ASPNETCORE_URLS='https://localhost:'+port;
  const output=fs.openSync(path.join(qa,'server.log'),'a'),error=fs.openSync(path.join(qa,'error.log'),'a');
  server=spawn('dotnet',[web],{cwd:path.join(root,'src','Finpulse.Web'),env,windowsHide:true,stdio:['ignore',output,error]});fs.closeSync(output);fs.closeSync(error);
  for(let i=0;i<60;i++){if(server.exitCode!==null)throw Error('El portal de pruebas no inicio; consulta '+qa);try{if((await request(new Map(),'/Account/Register')).status===200)return;}catch{}await new Promise(r=>setTimeout(r,500));}
  throw Error('El portal no respondio en HTTPS.');
}
(async()=>{
  // Verify the actual authenticated identity in C# before creating any synthetic accounts.
  const identity=spawnSync('dotnet',[web,'--verify-oracle'],{cwd:path.join(root,'src','Finpulse.Web'),env,encoding:'utf8',windowsHide:true});
  fs.writeFileSync(path.join(qa,'identity.log'),identity.stdout+identity.stderr);
  check(identity.status===0,'conexion C# autenticada como FINPULSE_WEB_TEST en FREEPDB1');
  const listener=net.createServer();listener.listen(0,'127.0.0.1');await once(listener,'listening');port=listener.address().port;await new Promise(r=>listener.close(r));
  await start();
  const a=new Map(),b=new Map(),name='qa_'+crypto.randomBytes(6).toString('hex'),password='clave-sintetica-muy-segura-123';
  const today=new Intl.DateTimeFormat('en-CA',{timeZone:'America/La_Paz'}).format(new Date());
  const deadline=(Number(today.slice(0,4))+1)+today.slice(4);
  check((await request(a,'/Finance/Index')).status===302,'anonimo sin acceso financiero');
  check((await request(a,'/Account/Login',{})).status===400,'CSRF rechazado');
  let page=await get(a,'/Account/Register');check(!page.includes('type="email"'),'registro sin correo');
  let r=await post(a,'/Account/Register',page,{Username:name,Password:password,Confirm:password});
  check(r.status===200&&r.body.includes('recovery-code'),'primer estudiante registrado');
  const recovery=r.body.match(/class="recovery-code">([^<]+)/)?.[1];
  check((r.headers['set-cookie']||[]).some(x=>x.startsWith('__Host-Finpulse=')&&/secure/i.test(x)&&/httponly/i.test(x)),'cookie Secure y HttpOnly');
  for(const module of ['Index','Movements','Budgets','Goals','Projections','Reports'])await get(a,'/Finance/'+module);
  page=await get(a,'/Finance/Movements');
  const income=page.match(/<option value="(\d+)" data-kind="0"/)[1],expense=page.match(/<option value="(\d+)" data-kind="1"/)[1];
  const movement={module:'movements',RequestKey:field(page,'RequestKey'),Kind:'0',CategoryId:income,Amount:'1000',Date:today,Description:'QA ingreso Oracle'};
  await saved(a,'/Finance/Save',page,movement,'ingreso Bs 1000 persistido');
  await saved(a,'/Finance/Save',page,movement,'reenvio idempotente');
  page=await get(a,'/Finance/Movements');check((page.match(/QA ingreso Oracle/g)||[]).length===1,'un solo ingreso tras reenvio');
  const id=field(page,'id');
  const register=await get(b,'/Account/Register');r=await post(b,'/Account/Register',register,{Username:name+'b',Password:password,Confirm:password});check(r.status===200,'segundo estudiante registrado');
  check((await request(b,'/Finance/Edit?module=movements&id='+id)).status===404,'edicion de movimiento ajeno rechazada');
  await saved(b,'/Finance/Delete',await get(b,'/Finance/Movements'),{module:'movements',id},'intento de borrado ajeno procesado sin acceso');
  check((await get(a,'/Finance/Movements')).includes('QA ingreso Oracle'),'movimiento del propietario conservado');
  page=await get(a,'/Finance/Edit?module=movements&id='+id);
  await saved(a,'/Finance/Save',page,{module:'movements',Id:id,Revision:field(page,'Revision'),RequestKey:field(page,'RequestKey'),Kind:'0',CategoryId:income,Amount:'1000',Date:today,Description:'QA editado Oracle'},'edicion propia persistida');
  page=await get(a,'/Finance/Movements');await saved(a,'/Finance/Save',page,{module:'movements',RequestKey:field(page,'RequestKey'),Kind:'1',CategoryId:expense,Amount:'250',Date:today,Description:''},'gasto Bs 250 y descripcion vacia');
  page=await get(a,'/Finance/Budgets');await saved(a,'/Finance/Save',page,{module:'budgets',RequestKey:field(page,'RequestKey'),Month:today.slice(0,7),Amount:'800'},'presupuesto persistido');
  page=await get(a,'/Finance/Goals');await saved(a,'/Finance/Save',page,{module:'goals',RequestKey:field(page,'RequestKey'),Name:'Equipo QA',Description:'',Amount:'1000',Date:deadline},'meta persistida');
  page=await get(a,'/Finance/Goals');const goal=field(page,'GoalId'),contributionPage=page.slice(page.indexOf('value="contributions"'));
  await saved(a,'/Finance/Save',page,{module:'contributions',RequestKey:field(contributionPage,'RequestKey'),GoalId:goal,Amount:'100',Date:today},'aporte Bs 100 persistido');
  page=await get(a,'/Finance/Projections');await saved(a,'/Finance/Scenario',page,{RequestKey:field(page,'RequestKey'),Name:'Escenario QA',Years:'5',Income:'1000',Expense:'250'},'escenario persistido');
  check((await get(a,'/Finance/Projections')).includes('45,000.00'),'proyeccion a cinco anios Bs 45000');
  check(/class="money"><span>Bs<\/span>\s*750[.,]00/.test(await get(a,'/Finance/Index')),'balance Bs 1000 menos Bs 250 = Bs 750');
  check(!(await get(b,'/Finance/Movements')).includes('QA editado Oracle'),'lectura aislada entre usuarios');
  await stop();await start();
  check(/class="money"><span>Bs<\/span>\s*750[.,]00/.test(await get(a,'/Finance/Index')),'saldo y sesion persisten tras reiniciar el proceso');
  check((await get(a,'/Finance/Goals')).includes('Equipo QA'),'meta persistente tras reinicio');
  check((await get(a,'/Finance/Projections')).includes('45,000.00'),'escenario persistente tras reinicio');
  check((await get(a,'/Finance/Export')).includes('QA editado Oracle'),'CSV del propietario');
  page=await get(a,'/Account/Settings');await saved(a,'/Account/Settings',page,{warning:'75'},'preferencias persistidas');
  await saved(a,'/Account/Logout',page,{},'cierre de sesion');check((await request(a,'/Finance/Index')).status===302,'cookie cerrada sin acceso');
  page=await get(a,'/Account/Login');await saved(a,'/Account/Login',page,{Username:name,Password:password},'inicio de sesion con usuario y contrasena');
  page=await get(b,'/Account/Recover');r=await post(b,'/Account/Recover',page,{Username:name,Password:password+'nueva',Confirm:password+'nueva',RecoveryCode:recovery});check(r.status===200&&r.body.includes('recovery-code'),'recuperacion cambia contrasena y codigo');
  check((await request(a,'/Finance/Index')).status===302,'recuperacion revoca sesion anterior');
  page=await get(a,'/Account/Login');await saved(a,'/Account/Login',page,{Username:name,Password:password+'nueva'},'acceso tras recuperacion');
  page=await get(a,'/Finance/Goals');const token=field(page,'__RequestVerificationToken');
  const attempts=await Promise.all([0,1].map(()=>request(a,'/Finance/Save',{__RequestVerificationToken:token,module:'contributions',RequestKey:crypto.randomBytes(16).toString('hex'),GoalId:goal,Amount:'400',Date:today})));
  check(attempts.filter(x=>x.status===302).length===1&&attempts.filter(x=>x.status===200).length===1,'dos aportes simultaneos Bs 400; solo uno aceptado');
  check(/Disponible despu[^<]*<strong>Bs 250[.,]00/.test(await get(a,'/Finance/Index')),'concurrencia conserva disponible Bs 250');
  page=await get(a,'/Finance/Movements');
  await saved(a,'/Finance/Save',page,{module:'movements',RequestKey:field(page,'RequestKey'),Kind:'0',CategoryId:income,Amount:'1',Date:today,Description:'QA temporal para eliminar'},'movimiento propio temporal guardado');
  page=await get(a,'/Finance/Movements');
  const temporaryId=field(page,'id');
  await saved(a,'/Finance/Delete',page,{module:'movements',id:temporaryId},'eliminacion propia persistida en Oracle');
  check(!(await get(a,'/Finance/Movements')).includes('QA temporal para eliminar'),'lectura confirma eliminacion propia');
  check(/class="money"><span>Bs<\/span>\s*750[.,]00/.test(await get(a,'/Finance/Index')),'eliminacion restaura balance Bs 750 sin afectar aportes');
  const result={Success:true,Provider:'Oracle',User:'FINPULSE_WEB_TEST',Container:'FREEPDB1',Username:name,Checks:count,Income:1000,Expense:250,Balance:750,Reserved:500,Available:250,RestartVerified:true,CheckedAt:new Date().toISOString()};
  fs.writeFileSync(path.join(root,'docs','qa','web-oracle-http-result.json'),JSON.stringify(result,null,2)+'\n');
  console.log(`OK: ${count} comprobaciones HTTPS con Oracle real. Reinicio, aislamiento y concurrencia verificados.`);
})().catch(e=>{console.error(e.message);process.exitCode=1;}).finally(stop);
