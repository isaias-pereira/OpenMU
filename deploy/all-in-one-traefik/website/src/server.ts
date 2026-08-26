import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import {join} from 'node:path';

const browserDistFolder = join(import.meta.dirname, '../browser');

const app = express();
app.use(express.json());

const angularApp = new AngularNodeAppEngine();

// In-Memory OpenMU Database for demo/portal
interface MockCharacter {
  id: string;
  name: string;
  className: string;
  classCode: string;
  guild: string;
  level: number;
  masterLevel: number;
  kills: number;
  resets: number;
  masterResets: number;
  strength: number;
  agility: number;
  vitality: number;
  energy: number;
  command?: number;
  status: string;
}

interface MockAccount {
  id: string;
  loginName: string;
  passwordHash: string;
  email: string;
  securityCode: string;
  registrationDate: string;
  state: number;
  characters: MockCharacter[];
}

const mockAccounts: MockAccount[] = [
  {
    id: 'acc-1',
    loginName: 'admin',
    passwordHash: '123456',
    email: 'admin@mufree.com',
    securityCode: '1234',
    registrationDate: new Date().toISOString(),
    state: 0,
    characters: [
      {
        id: 'c1',
        name: 'DarkVader',
        className: 'Blade Knight',
        classCode: 'BK',
        guild: 'BloodLords',
        level: 400,
        masterLevel: 150,
        kills: 142,
        resets: 85,
        masterResets: 5,
        strength: 1800,
        agility: 1200,
        vitality: 900,
        energy: 600,
        status: 'Online'
      },
      {
        id: 'c2',
        name: 'SoulReaper',
        className: 'Soul Master',
        classCode: 'SM',
        guild: 'BloodLords',
        level: 380,
        masterLevel: 110,
        kills: 89,
        resets: 62,
        masterResets: 2,
        strength: 650,
        agility: 1100,
        vitality: 800,
        energy: 2200,
        status: 'Offline'
      },
      {
        id: 'c3',
        name: 'WindGoddess',
        className: 'Muse Elf',
        classCode: 'ME',
        guild: 'SilverThorns',
        level: 395,
        masterLevel: 130,
        kills: 67,
        resets: 74,
        masterResets: 3,
        strength: 700,
        agility: 2100,
        vitality: 750,
        energy: 950,
        status: 'Offline'
      }
    ]
  },
  {
    id: 'acc-2',
    loginName: 'player1',
    passwordHash: '123456',
    email: 'player1@mufree.com',
    securityCode: '1234',
    registrationDate: new Date().toISOString(),
    state: 0,
    characters: [
      {
        id: 'c4',
        name: 'LordSauron',
        className: 'Dark Lord',
        classCode: 'DL',
        guild: 'Imperium',
        level: 398,
        masterLevel: 145,
        kills: 115,
        resets: 80,
        masterResets: 4,
        strength: 1500,
        agility: 900,
        vitality: 1000,
        energy: 1400,
        command: 950,
        status: 'Offline'
      }
    ]
  }
];

const mockRankings = [
  { position: 1, characterName: 'DarkVader', guildName: 'BloodLords', level: 400, className: 'Blade Knight', resets: 85 },
  { position: 2, characterName: 'LordSauron', guildName: 'Imperium', level: 398, className: 'Dark Lord', resets: 80 },
  { position: 3, characterName: 'WindGoddess', guildName: 'SilverThorns', level: 395, className: 'Muse Elf', resets: 74 },
  { position: 4, characterName: 'ArcaneEmperor', guildName: 'MysticOrder', level: 390, className: 'Soul Master', resets: 70 },
  { position: 5, characterName: 'ChaosGladiator', guildName: 'Valhalla', level: 388, className: 'Magic Gladiator', resets: 68 },
];

const mockDeathsRankings = [
  { position: 1, characterName: 'DarkVader', guildName: 'BloodLords', kills: 142, className: 'Blade Knight' },
  { position: 2, characterName: 'LordSauron', guildName: 'Imperium', kills: 115, className: 'Dark Lord' },
  { position: 3, characterName: 'SoulReaper', guildName: 'BloodLords', kills: 89, className: 'Soul Master' },
  { position: 4, characterName: 'ChaosGladiator', guildName: 'Valhalla', kills: 78, className: 'Magic Gladiator' },
  { position: 5, characterName: 'WindGoddess', guildName: 'SilverThorns', kills: 67, className: 'Muse Elf' },
];

// Active sessions
const activeSessions = new Map<string, string>(); // token -> loginName

// REST API Endpoints
app.get('/api/health', (req, res) => {
  res.json({ status: 'healthy', database: 'connected' });
});

app.get('/api/server-info', (req, res) => {
  res.json({
    serverName: 'MU FREE',
    season: 'Season 6',
    version: 'Episódio 3 (S6E3)',
    totalAccounts: mockAccounts.length + 1248,
    normalAccounts: mockAccounts.length + 1200,
    onlinePlayers: 342,
    peakOnline: 890,
    expRate: '150x',
    dropRate: '40%',
    status: 'online',
    castlesiege: 'Domingo às 20:00 (LorenDeep)',
    bloodCastle: 'A cada 2 horas',
    chaosCastle: 'A cada 4 horas',
    devilSquare: 'A cada 2 horas'
  });
});

app.get('/api/ranking', (req, res) => {
  res.json({
    success: true,
    ranking: mockRankings
  });
});

app.get('/api/ranking/deaths', (req, res) => {
  res.json({
    success: true,
    ranking: mockDeathsRankings
  });
});

app.post('/api/register', (req, res): void => {
  const { loginName, password, email, securityCode } = req.body || {};

  if (!loginName || !password || !email || !securityCode) {
    res.status(400).json({ success: false, message: 'Todos os campos são obrigatórios' });
    return;
  }

  if (!/^[a-zA-Z0-9]{3,10}$/.test(loginName)) {
    res.status(400).json({ success: false, message: 'Login deve ter entre 3 e 10 caracteres alfanuméricos' });
    return;
  }

  if (password.length < 6) {
    res.status(400).json({ success: false, message: 'Senha deve ter pelo menos 6 caracteres' });
    return;
  }

  const existingUser = mockAccounts.find(a => a.loginName.toLowerCase() === loginName.toLowerCase());
  if (existingUser) {
    res.status(409).json({ success: false, message: 'Login já está em uso' });
    return;
  }

  const existingEmail = mockAccounts.find(a => a.email.toLowerCase() === email.toLowerCase());
  if (existingEmail) {
    res.status(409).json({ success: false, message: 'E-mail já está cadastrado' });
    return;
  }

  const newAcc: MockAccount = {
    id: `acc-${Date.now()}`,
    loginName,
    passwordHash: password,
    email,
    securityCode,
    registrationDate: new Date().toISOString(),
    state: 0,
    characters: [
      {
        id: `char-${Date.now()}`,
        name: loginName + '_DK',
        className: 'Dark Knight',
        classCode: 'DK',
        guild: 'Iniciantes',
        level: 1,
        masterLevel: 0,
        kills: 0,
        resets: 0,
        masterResets: 0,
        strength: 28,
        agility: 20,
        vitality: 25,
        energy: 10,
        status: 'Offline'
      }
    ]
  };

  mockAccounts.push(newAcc);

  res.status(201).json({
    success: true,
    message: 'Conta criada com sucesso',
    account: {
      id: newAcc.id,
      loginName: newAcc.loginName,
      email: newAcc.email,
      registrationDate: newAcc.registrationDate
    }
  });
});

app.post('/api/login', (req, res): void => {
  const { loginName, password } = req.body || {};

  if (!loginName || !password) {
    res.status(400).json({ success: false, message: 'Informe login e senha' });
    return;
  }

  const account = mockAccounts.find(
    a => a.loginName.toLowerCase() === loginName.toLowerCase() && a.passwordHash === password
  );

  if (!account) {
    res.status(401).json({ success: false, message: 'Login ou senha inválidos' });
    return;
  }

  const token = `token-${account.id}-${Date.now()}`;
  activeSessions.set(token, account.loginName);

  res.json({
    success: true,
    token,
    loginName: account.loginName
  });
});

app.post('/api/logout', (req, res): void => {
  const authHeader = req.headers.authorization;
  if (authHeader) {
    const token = authHeader.replace('Bearer ', '');
    activeSessions.delete(token);
  }
  res.json({ success: true });
});

app.get('/api/me', (req, res): void => {
  const authHeader = req.headers.authorization;
  const token = authHeader ? authHeader.replace('Bearer ', '') : '';
  const loginName = activeSessions.get(token) || req.query['user'] as string || 'admin';

  const account = mockAccounts.find(a => a.loginName.toLowerCase() === loginName.toLowerCase());

  if (!account) {
    res.status(401).json({ success: false, message: 'Faça login para continuar' });
    return;
  }

  res.json({
    success: true,
    account: {
      loginName: account.loginName,
      email: account.email,
      characters: account.characters
    }
  });
});

/**
 * Serve static files from /browser
 */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);

/**
 * Handle all other requests by rendering the Angular application.
 */
app.use((req, res, next) => {
  angularApp
    .handle(req)
    .then((response) =>
      response ? writeResponseToNodeResponse(response, res) : next(),
    )
    .catch(next);
});

/**
 * Start the server if this module is the main entry point, or it is ran via PM2.
 * The server listens on the port defined by the `PORT` environment variable, or defaults to 4000.
 */
if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

/**
 * Request handler used by the Angular CLI (for dev-server and during build) or Firebase Cloud Functions.
 */
export const reqHandler = createNodeRequestHandler(app);
