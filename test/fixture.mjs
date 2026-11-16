import {createCloud} from '../../minova-cloud/server.mjs';
import fs from 'node:fs/promises';import os from 'node:os';import path from 'node:path';
const directory=await fs.mkdtemp(path.join(os.tmpdir(),'minova-windows-server-'));const storage=path.join(directory,'storage');await fs.mkdir(storage);
const mailer={async status(){return {configured:false};},async sendInvitation(){throw Error('Email disabled in Windows integration fixture');}};
const cloud=await createCloud({storageRoot:storage,dataDir:path.join(directory,'data'),port:4197,mailer});await cloud.listen();
const response=await fetch('http://127.0.0.1:4197/api/setup',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:'windows_test',name:'Windows Test',password:'synthetic windows passphrase'})});if(response.status!==201)throw Error('Fixture setup failed');
console.log('Synthetic Windows integration server ready on 4197');
process.on('SIGINT',async()=>{await cloud.close();process.exit(0);});
