(function(){
  var catalog=window.__CATALOG__||{mods:[]};
  var mods=catalog.mods||[];
  var activeCategory="All";
  function byId(id){return document.getElementById(id)}
  function text(tag,value,cls){var n=document.createElement(tag);if(cls)n.className=cls;n.appendChild(document.createTextNode(value||""));return n}
  function formatSize(bytes){if(!bytes)return "SIZE UNKNOWN";var units=["B","KB","MB","GB"];var i=0,v=bytes;while(v>=1024&&i<3){v/=1024;i++}return v.toFixed(v>=10?0:1)+" "+units[i]}
  function categories(){var seen={All:true},out=["All"];for(var i=0;i<mods.length;i++){var c=mods[i].category||"Miscellaneous";if(!seen[c]){seen[c]=true;out.push(c)}}return out}
  function renderChips(){var host=byId("chips");host.innerHTML="";var all=categories();for(var i=0;i<all.length;i++){(function(category){var b=text("button",category,"chip"+(category===activeCategory?" active":""));b.onclick=function(){activeCategory=category;renderChips();applyFilters()};host.appendChild(b)})(all[i])}}
  function makeCard(mod,index){var wrap=document.createElement("div");wrap.className="card-wrap";var card=document.createElement("article");card.className="card";card.style.animationDelay=(index*55)+"ms";
    var cover=document.createElement("div");cover.className="cover";if(mod.thumbnailUrl)cover.style.backgroundImage='linear-gradient(transparent 45%,#111824),url("'+String(mod.thumbnailUrl).replace(/"/g,"")+'")';
    cover.appendChild(text("span",mod.category||"Misc","category"));cover.appendChild(text("span","v"+(mod.version||"1.0"),"version"));cover.appendChild(text("span","AC","cover-mark"));card.appendChild(cover);
    var body=document.createElement("div");body.className="card-body";body.appendChild(text("h3",mod.name));body.appendChild(text("div","by "+(mod.author||"Unknown"),"author"));body.appendChild(text("p",mod.description||"Assetto Corsa mod package","description"));
    var tags=document.createElement("div");tags.className="tags";var list=mod.tags||[];for(var t=0;t<list.length;t++)tags.appendChild(text("span",list[t],"tag"));body.appendChild(tags);card.appendChild(body);
    var footer=document.createElement("div");footer.className="card-footer";footer.appendChild(text("span",formatSize(mod.expectedSize),"size"));var button=text("button",mod.isAvailable!==false&&mod.downloadUrl?"DOWNLOAD & INSTALL":"COMING SOON","install");if(!(mod.downloadUrl)){button.disabled=true}else{button.onclick=function(){installMod(mod.id)}}footer.appendChild(button);card.appendChild(footer);wrap.appendChild(card);return wrap}
  window.applyFilters=function(){var q=(byId("search").value||"").toLowerCase();var host=byId("cards");host.innerHTML="";var count=0;for(var i=0;i<mods.length;i++){var m=mods[i];var hay=[m.name,m.author,m.description,(m.tags||[]).join(" ")].join(" ").toLowerCase();if((activeCategory==="All"||m.category===activeCategory)&&(!q||hay.indexOf(q)>=0)){host.appendChild(makeCard(m,count));count++}}byId("empty").className=count?"empty glass hidden":"empty glass"};
  window.installMod=function(id){showToast("Preparing secure download","Validating catalog metadata…");try{window.external.InstallMod(id)}catch(e){showToast("Bridge unavailable",String(e.message||e))}};
  window.refreshCatalog=function(){showToast("Refreshing catalog","Checking embedded and remote sources…");try{window.external.RefreshCatalog()}catch(e){showToast("Refresh unavailable",String(e.message||e))}};
  window.focusCatalog=function(){byId("catalog").scrollIntoView(true)};
  window.showToast=function(title,message){byId("toastTitle").innerText=title;byId("toastText").innerText=message;byId("toast").className="toast show"};
  window.catalogProgress=function(json){try{var p=typeof json==="string"?JSON.parse(json):json;showToast(p.title||"Downloading",p.message||"");if(p.done){setTimeout(function(){byId("toast").className="toast"},1800)}}catch(e){}}
  byId("modCount").innerText=String(mods.length);byId("catalogSource").innerText=String(window.__CATALOG_SOURCE__||"embedded").toUpperCase();byId("catalogDate").innerText=catalog.updatedAt?String(catalog.updatedAt).substring(0,10):"ready";
  if(window.__CATALOG_WARNING__){byId("warning").innerText=window.__CATALOG_WARNING__;byId("warning").className="warning"}
  renderChips();applyFilters();
})();