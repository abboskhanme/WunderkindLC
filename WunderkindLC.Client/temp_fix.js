const fs = require('fs')
const path = 'src/components/chat/ChatPanel.tsx'
let content = fs.readFileSync(path, 'utf8')
// Replace single-quoted strings containing Uzbek apostrophe with double-quoted versions
content = content.replace(/'Xabarlar yuklab bo[‘’']lmadi'/g, '"Xabarlar yuklab bo' + "'" + 'lmadi"')
content = content.replace(/'Fayl yuklab bo[‘’']lmadi'/g, '"Fayl yuklab bo' + "'" + 'lmadi"')
fs.writeFileSync(path, content, 'utf8')
console.log('Fixed')
