import fs from 'node:fs/promises';
const repository=process.env.GITHUB_REPOSITORY,token=process.env.GITHUB_TOKEN;
if(repository!=='minova-chromium/Minova-Cloud-Desktop'||!token)throw Error('Run this in the Minova Windows repository.');
const metadata=JSON.parse((await fs.readFile('releases/release.json','utf8')).replace(/^\uFEFF/,''));
if(!/^\d+\.\d+\.\d+$/.test(metadata.version))throw Error('Invalid Windows version');
const headers={Authorization:`Bearer ${token}`,Accept:'application/vnd.github+json','User-Agent':'Minova-Cloud-Desktop'};
async function api(route,method='GET',body){const response=await fetch('https://api.github.com/repos/'+repository+route,{method,headers:{...headers,'Content-Type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});if(!response.ok)throw Error(`GitHub ${method} failed (${response.status}): ${await response.text()}`);return response.status===204?null:response.json();}
const releases=await api('/releases?per_page=100');const tag='v'+metadata.version;let release=releases.find(x=>x.tag_name===tag);
if(release&&!release.draft){console.log('This Windows version is already published. Increase the project version to release another build.');process.exit(0);}
const body='Desktop 0.3.0: Built-in automatic updates and the approved Minova Cloud logo. Published stable updates are verified with Minova RSA-signed metadata and SHA-256, then installed when sync is idle. Files and sign-in are retained.\n\nInstall Minova-Cloud-Setup.exe once to enable the updater. Native File Explorer sync and Files, Music, Videos, Pictures folders are included. Windows 10 2004+ / Windows 11, x64, NTFS. The installer is not Authenticode-signed. Review and publish this draft when ready.';
if(!release)release=await api('/releases','POST',{tag_name:tag,target_commitish:process.env.GITHUB_SHA,name:'Minova Cloud Desktop '+metadata.version,body,draft:true,prerelease:false});
else release=await api('/releases/'+release.id,'PATCH',{tag_name:tag,target_commitish:process.env.GITHUB_SHA,name:'Minova Cloud Desktop '+metadata.version,body,draft:true,prerelease:false});
for(const name of ['Minova-Cloud-Setup.exe','Minova-Cloud-Desktop-'+metadata.version+'.exe','release.json','release.json.sig','SHA256SUMS.txt']){
 const previous=release.assets.find(x=>x.name===name);if(previous)await api('/releases/assets/'+previous.id,'DELETE');
 const response=await fetch(`https://uploads.github.com/repos/${repository}/releases/${release.id}/assets?name=${encodeURIComponent(name)}`,{method:'POST',headers:{...headers,'Content-Type':'application/octet-stream'},body:await fs.readFile('releases/'+name)});if(!response.ok)throw Error('Could not upload '+name);
}
await fs.appendFile(process.env.GITHUB_STEP_SUMMARY,`## Windows ${metadata.version}\n\nDraft installer ready for review. Publishing it makes it the latest Windows release.\n\n[View releases](https://github.com/${repository}/releases)\n`);console.log('Windows draft ready.');
