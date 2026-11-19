using System.Net;
using System.Text.RegularExpressions;

internal static class HttpChecks
{
    public static async Task Run(string origin)
    {
        if (!Uri.TryCreate(origin,UriKind.Absolute,out var uri) || !uri.IsLoopback || uri.Scheme!="https") throw new ArgumentException("Solo servidor HTTPS local con base sintética.");
        int count=0;
        void Check(bool ok,string title){if(!ok)throw new Exception(title);Console.WriteLine($"HTTP {++count}: {title}");}
        HttpClient Client()=>new(new HttpClientHandler{AllowAutoRedirect=false,CookieContainer=new CookieContainer(),ServerCertificateCustomValidationCallback=(_,_,_,_)=>true}){BaseAddress=uri};
        using var client=Client();using var other=Client();
        async Task<string> Get(HttpClient c,string path){var r=await c.GetAsync(path);Check(r.StatusCode==HttpStatusCode.OK,$"GET {path}");return await r.Content.ReadAsStringAsync();}
        string Field(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,$"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
        async Task<HttpResponseMessage> Post(HttpClient c,string path,string page,Dictionary<string,string> fields){fields["__RequestVerificationToken"]=Field(page,"__RequestVerificationToken");return await c.PostAsync(path,new FormUrlEncodedContent(fields));}
        Check((await client.GetAsync("/Finance/Index")).StatusCode==HttpStatusCode.Redirect,"anónimo redirigido a acceso");
        Check((await client.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode==HttpStatusCode.BadRequest,"CSRF sin token rechazado");
        var name="qa_"+Guid.NewGuid().ToString("N")[..12]; const string password="clave-sintetica-muy-segura-123";
        var register=await Get(client,"/Account/Register");
        Check(!register.Contains("type=\"email\""),"registro no solicita correo");
        var result=await Post(client,"/Account/Register",register,new(){{"Username",name},{"Password",password},{"Confirm",password}});
        var codePage=await result.Content.ReadAsStringAsync();Check(result.StatusCode==HttpStatusCode.OK&&codePage.Contains("recovery-code"),"registro web entrega recuperación");
        var code=Regex.Match(codePage,"class=\"recovery-code\">([^<]+)").Groups[1].Value;
        Check(result.Headers.GetValues("Set-Cookie").Any(x=>x.StartsWith("__Host-Finpulse=")&&x.Contains("secure",StringComparison.OrdinalIgnoreCase)&&x.Contains("httponly",StringComparison.OrdinalIgnoreCase)),"cookie Secure y HttpOnly");
        foreach(var route in new[]{"Index","Movements","Budgets","Goals","Projections","Reports"})await Get(client,"/Finance/"+route);
        await Get(client,"/Account/Settings");
        var page=await Get(client,"/Finance/Movements");
        var category=Regex.Match(page,"<option value=\"(\\d+)\" data-kind=\"0\"").Groups[1].Value;
        var today=Finpulse.Web.Services.PortalFinance.Today.ToString("yyyy-MM-dd");
        Dictionary<string,string> movement=new(){{"module","movements"},{"RequestKey",Field(page,"RequestKey")},{"Kind","0"},{"CategoryId",category},{"Amount","1200"},{"Date",today},{"Description","QA real HTTP"}};
        Check((await Post(client,"/Finance/Save",page,movement)).StatusCode==HttpStatusCode.Redirect,"ingreso web guardado");
        Check((await Post(client,"/Finance/Save",page,movement)).StatusCode==HttpStatusCode.Redirect,"reenvío web tolerado");
        page=await Get(client,"/Finance/Movements");Check(Regex.Matches(page,"QA real HTTP").Count==1,"reenvío no duplica ingreso");
        var id=Regex.Match(page,"name=\"id\" value=\"(\\d+)\"").Groups[1].Value;
        var r2=await Get(other,"/Account/Register");var second=await Post(other,"/Account/Register",r2,new(){{"Username",name+"b"},{"Password",password},{"Confirm",password}});Check(second.StatusCode==HttpStatusCode.OK,"segunda cuenta sintética");
        Check((await other.GetAsync("/Finance/Edit?module=movements&id="+id)).StatusCode==HttpStatusCode.NotFound,"edición cruzada HTTP rechazada");
        var otherPage=await Get(other,"/Finance/Movements");await Post(other,"/Finance/Delete",otherPage,new(){{"module","movements"},{"id",id}});
        Check((await Get(client,"/Finance/Movements")).Contains("QA real HTTP"),"borrado cruzado no afecta al propietario");
        var edit=await Get(client,"/Finance/Edit?module=movements&id="+id);
        var editResult=await Post(client,"/Finance/Save",edit,new(){{"module","movements"},{"Id",id},{"Revision",Field(edit,"Revision")},{"RequestKey",Field(edit,"RequestKey")},{"Kind","0"},{"CategoryId",category},{"Amount","1500"},{"Date",today},{"Description","QA editado"}});
        Check(editResult.StatusCode==HttpStatusCode.Redirect&&(await Get(client,"/Finance/Movements")).Contains("QA editado"),"edición propia persistida");
        page=await Get(client,"/Finance/Movements");var expenseCategory=Regex.Match(page,"<option value=\"(\\d+)\" data-kind=\"1\"").Groups[1].Value;
        Check((await Post(client,"/Finance/Save",page,new(){{"module","movements"},{"RequestKey",Field(page,"RequestKey")},{"Kind","1"},{"CategoryId",expenseCategory},{"Amount","850"},{"Date",today},{"Description",""}})).StatusCode==HttpStatusCode.Redirect,"gasto web guardado con descripción opcional vacía");
        page=await Get(client,"/Finance/Budgets");Check((await Post(client,"/Finance/Save",page,new(){{"module","budgets"},{"RequestKey",Field(page,"RequestKey")},{"Month",today[..7]},{"Amount","800"}})).StatusCode==HttpStatusCode.Redirect,"presupuesto web guardado");
        page=await Get(client,"/Finance/Goals");Check((await Post(client,"/Finance/Save",page,new(){{"module","goals"},{"RequestKey",Field(page,"RequestKey")},{"Name","Equipo QA"},{"Description",""},{"Amount","1000"},{"Date",Finpulse.Web.Services.PortalFinance.Today.AddYears(1).ToString("yyyy-MM-dd")}})).StatusCode==HttpStatusCode.Redirect,"meta web guardada con descripción opcional vacía");
        page=await Get(client,"/Finance/Goals");var goalId=Field(page,"GoalId");var contributionForm=page[page.IndexOf("value=\"contributions\"",StringComparison.Ordinal)..];
        Check((await Post(client,"/Finance/Save",page,new(){{"module","contributions"},{"GoalId",goalId},{"RequestKey",Field(contributionForm,"RequestKey")},{"Amount","100"},{"Date",today}})).StatusCode==HttpStatusCode.Redirect,"aporte web guardado");
        page=await Get(client,"/Finance/Projections");Check((await Post(client,"/Finance/Scenario",page,new(){{"RequestKey",Field(page,"RequestKey")},{"Name","Escenario QA"},{"Years","5"},{"Income","1200"},{"Expense","850"}})).StatusCode==HttpStatusCode.Redirect,"escenario web calculado");
        Check((await Get(client,"/Finance/Projections")).Contains("21,000.00"),"resultado de proyección real en HTML");
        if(Environment.GetEnvironmentVariable("FINPULSE_QA_SNAPSHOTS") is string snapshotDirectory) {
            Directory.CreateDirectory(snapshotDirectory);
            var assets=new Uri(Path.GetFullPath("src/Finpulse.Web/wwwroot")+Path.DirectorySeparatorChar).AbsoluteUri;
            foreach(var route in new[]{"Index","Movements","Budgets","Goals","Projections","Reports"}) {
                var html=await Get(client,"/Finance/"+route);
                foreach(var prefix in new[]{"/css/","/js/","/lib/","/mark.svg"}) html=html.Replace("\""+prefix,"\""+assets+prefix.TrimStart('/'));
                html=Regex.Replace(html,"(<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\")[^\"]*","$1REMOVED_FOR_QA");
                await File.WriteAllTextAsync(Path.Combine(snapshotDirectory,route+".html"),html);
            }
        }
        var export=await client.GetAsync("/Finance/Export");Check(export.StatusCode==HttpStatusCode.OK&&(await export.Content.ReadAsStringAsync()).Contains("QA editado"),"CSV contiene movimientos del propietario");
        page=await Get(client,"/Account/Settings");Check((await Post(client,"/Account/Settings",page,new(){{"warning","75"}})).StatusCode==HttpStatusCode.Redirect,"preferencias guardadas");
        Check((await Post(client,"/Account/Logout",page,new())).StatusCode==HttpStatusCode.Redirect,"cierre de sesión por POST");
        Check((await client.GetAsync("/Finance/Index")).StatusCode==HttpStatusCode.Redirect,"sesión cerrada pierde acceso");
        page=await Get(client,"/Account/Login");Check((await Post(client,"/Account/Login",page,new(){{"Username",name},{"Password",password}})).StatusCode==HttpStatusCode.Redirect,"inicio de sesión web");
        page=await Get(other,"/Account/Recover");var reset=await Post(other,"/Account/Recover",page,new(){{"Username",name},{"Password",password+"nueva"},{"Confirm",password+"nueva"},{"RecoveryCode",code}});
        Check(reset.StatusCode==HttpStatusCode.OK&&(await reset.Content.ReadAsStringAsync()).Contains("recovery-code"),"recuperación web renueva código");
        Check((await client.GetAsync("/Finance/Index")).StatusCode==HttpStatusCode.Redirect,"recuperación revoca cookie anterior");
        Console.WriteLine($"OK: {count} comprobaciones HTTP sobre base aislada.");
    }
}
