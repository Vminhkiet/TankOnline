import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  Activity,
  Ban,
  Bell,
  Bolt,
  Cpu,
  Gauge,
  Gamepad2,
  Globe2,
  LineChart,
  Map,
  Play,
  Radar,
  RefreshCcw,
  Search,
  Server,
  Shield,
  Skull,
  TerminalSquare,
  UserPlus,
  Users,
  Wallet,
  Wifi,
  XCircle,
  Store,
  Trophy,
  History,
  Eye
} from 'lucide-react';
import './styles.css';

const API_BASE = 'http://localhost:8080';

const logsSeed = [
  { level: 'error', time: '14:22:10', playerId: 'TL-3130', source: 'physics', message: 'Tank collider escaped nav bounds near Bridge' },
  { level: 'warning', time: '14:21:42', playerId: 'TL-1042', source: 'match', message: 'High shell fire rate detected for 8 seconds' },
  { level: 'info', time: '14:21:05', playerId: 'system', source: 'server', message: 'Map changed to Desert Foundry' },
  { level: 'info', time: '14:20:31', playerId: 'TL-2201', source: 'economy', message: 'Currency transaction completed: +450 coins' },
  { level: 'warning', time: '14:19:57', playerId: 'TL-6095', source: 'network', message: 'Ping jitter above threshold in Asia shard' },
  { level: 'error', time: '14:18:44', playerId: 'TL-4044', source: 'auth', message: 'Session token expired during reconnect' }
];

const transactions = [
  { id: 'TX-8812', player: 'IronMek', type: 'reward', amount: '+1,200', status: 'settled' },
  { id: 'TX-8811', player: 'NovaShell', type: 'purchase', amount: '-800', status: 'settled' },
  { id: 'TX-8810', player: 'ByteTiger', type: 'refund', amount: '+300', status: 'review' },
  { id: 'TX-8809', player: 'TankDad', type: 'admin grant', amount: '+5,000', status: 'settled' }
];

const maps = ['Arena', 'Desert', 'Frozen Basin', 'Skyline Siege'];
const matchesSeed = [
  {
    id: '001',
    name: 'Ranked Alpha',
    map: 'Arena',
    mode: '5v5 Control',
    status: 'Running',
    players: [
      { id: 'TL-1042', pos: 'A7', hp: 92, state: 'Combat', ping: 34 },
      { id: 'TL-2201', pos: 'C2', hp: 76, state: 'Moving', ping: 58 },
      { id: 'TL-3130', pos: 'B5', hp: 41, state: 'Reloading', ping: 126 },
      { id: 'TL-6095', pos: 'E4', hp: 88, state: 'Defending', ping: 71 },
      { id: 'BOT-017', pos: 'D3', hp: 100, state: 'Patrol', ping: 0 }
    ],
    maxPlayers: 10,
    score: '42 - 38',
    duration: '12:44',
    region: 'Asia',
    tickRate: 60
  },
  {
    id: '002',
    name: 'Casual Bravo',
    map: 'Desert',
    mode: '8v8 Siege',
    status: 'Waiting',
    players: [
      { id: 'TL-5188', pos: 'Lobby', hp: 100, state: 'Ready', ping: 22 },
      { id: 'TL-4044', pos: 'Lobby', hp: 100, state: 'Loading', ping: 0 }
    ],
    maxPlayers: 10,
    score: '0 - 0',
    duration: '01:18',
    region: 'Asia',
    tickRate: 60
  },
  {
    id: '003',
    name: 'Dev Sandbox',
    map: 'Frozen Basin',
    mode: 'Debug Arena',
    status: 'Paused',
    players: [
      { id: 'TL-5188', pos: 'S1', hp: 100, state: 'Debug', ping: 18 }
    ],
    maxPlayers: 10,
    score: '12 - 4',
    duration: '34:02',
    region: 'Local',
    tickRate: 30
  },
  {
    id: '004',
    name: 'Tournament Delta',
    map: 'Skyline Siege',
    mode: '3v3 Elimination',
    status: 'Ended',
    players: [
      { id: 'TL-1042', pos: 'A1', hp: 0, state: 'Eliminated', ping: 36 },
      { id: 'TL-6095', pos: 'B8', hp: 64, state: 'Victory', ping: 69 },
      { id: 'TL-2201', pos: 'C4', hp: 21, state: 'Victory', ping: 55 }
    ],
    maxPlayers: 10,
    score: '5 - 3',
    duration: '09:51',
    region: 'Asia',
    tickRate: 60
  }
];
const roleProfiles = [
  { role: 'admin', access: 'Full control', users: 3, color: 'red' },
  { role: 'moderator', access: 'Player actions, logs', users: 8, color: 'blue' },
  { role: 'dev', access: 'Debug tools, configs', users: 5, color: 'green' }
];

function App() {
  const [session, setSession] = useState(null);
  const [players, setPlayers] = useState([]);
  const [playersLoading, setPlayersLoading] = useState(false);
  const [query, setQuery] = useState('');
  const [logFilter, setLogFilter] = useState('all');
  const [playerLogQuery, setPlayerLogQuery] = useState('');
  const [selectedMap, setSelectedMap] = useState(maps[0]);
  const [selectedMatchId, setSelectedMatchId] = useState(matchesSeed[0].id);
  const [commandFeed, setCommandFeed] = useState(['Debug console ready', 'Connected to asia-main-01']);
  const [config, setConfig] = useState({ speed: 100, damage: 75, spawnRate: 42, realtime: true });
  const consoleRef = useRef(null);
  const shouldFollowConsoleRef = useRef(true);

  // Leaderboard state
  const [leaderboard, setLeaderboard] = useState([]);
  const [leaderboardLoading, setLeaderboardLoading] = useState(false);

  // Match history lookup state
  const [historyPlayerId, setHistoryPlayerId] = useState('');
  const [playerHistory, setPlayerHistory] = useState([]);
  const [playerStats, setPlayerStats] = useState(null);
  const [historyLoading, setHistoryLoading] = useState(false);

  // Fetch real players from Auth Service
  const fetchPlayers = useCallback(async () => {
    try {
      setPlayersLoading(true);
      const res = await fetch(`${API_BASE}/api/user/users`);
      if (res.ok) {
        const data = await res.json();
        setPlayers(data.map(u => ({
          playerId: `UID-${u.id}`,
          rawId: u.id,
          username: u.username,
          email: u.email || '-',
          role: u.role ? u.role.replace('ROLE_', '').toLowerCase() : 'user',
          address: u.address || null
        })));
      }
    } catch (err) {
      console.error('Failed to fetch players:', err);
    } finally {
      setPlayersLoading(false);
    }
  }, []);

  // Fetch leaderboard from History Service
  const fetchLeaderboard = useCallback(async () => {
    try {
      setLeaderboardLoading(true);
      const res = await fetch(`${API_BASE}/api/history/leaderboard`);
      if (res.ok) {
        setLeaderboard(await res.json());
      }
    } catch (err) {
      console.error('Failed to fetch leaderboard:', err);
    } finally {
      setLeaderboardLoading(false);
    }
  }, []);

  // Fetch per-player history + stats
  const fetchPlayerHistory = useCallback(async (pid) => {
    if (!pid) return;
    try {
      setHistoryLoading(true);
      const [histRes, statsRes] = await Promise.all([
        fetch(`${API_BASE}/api/history/player/${pid}`),
        fetch(`${API_BASE}/api/history/player/${pid}/stats`)
      ]);
      if (histRes.ok) setPlayerHistory(await histRes.json());
      if (statsRes.ok) setPlayerStats(await statsRes.json());
    } catch (err) {
      console.error('Failed to fetch player history:', err);
    } finally {
      setHistoryLoading(false);
    }
  }, []);

  // Initial data load
  useEffect(() => {
    fetchPlayers();
    fetchLeaderboard();
  }, [fetchPlayers, fetchLeaderboard]);

  const filteredPlayers = useMemo(() => {
    const value = query.trim().toLowerCase();
    if (!value) return players;
    return players.filter((player) =>
      [
        player.playerId,
        player.username,
        player.email,
        player.role
      ].some((item) =>
        String(item).toLowerCase().includes(value)
      )
    );
  }, [players, query]);

  const filteredLogs = useMemo(() => {
    return logsSeed.filter((log) => {
      const levelMatch = logFilter === 'all' || log.level === logFilter;
      const playerMatch = !playerLogQuery || log.playerId.toLowerCase().includes(playerLogQuery.toLowerCase());
      return levelMatch && playerMatch;
    });
  }, [logFilter, playerLogQuery]);

  const totalPlayers = players.length;
  const selectedMatch = matchesSeed.find((match) => match.id === selectedMatchId) ?? matchesSeed[0];

  useEffect(() => {
    if (consoleRef.current && shouldFollowConsoleRef.current) {
      consoleRef.current.scrollTop = consoleRef.current.scrollHeight;
    }
  }, [commandFeed]);

  function handleConsoleScroll(event) {
    const element = event.currentTarget;
    const distanceFromBottom = element.scrollHeight - element.scrollTop - element.clientHeight;
    shouldFollowConsoleRef.current = distanceFromBottom < 12;
  }

  if (!session) {
    return <LoginScreen onLogin={setSession} />;
  }

  function pushCommand(text) {
    setCommandFeed((items) => [...items, `${new Date().toLocaleTimeString()} - ${text}`]);
  }

  function kickPlayer(playerId) {
    setPlayers((items) =>
      items.map((player) =>
        player.playerId === playerId ? { ...player, onlineStatus: 'offline', matchId: '' } : player
      )
    );
    pushCommand(`Kicked ${playerId}`);
  }

  function toggleBanPlayer(playerId) {
    setPlayers((items) =>
      items.map((player) =>
        player.playerId === playerId
          ? {
              ...player,
              isBanned: !player.isBanned,
              banReason: player.isBanned ? '' : 'Manual admin ban',
              banExpires: player.isBanned ? '' : '2026-05-09 00:00',
              onlineStatus: player.isBanned ? player.onlineStatus : 'offline',
              matchId: player.isBanned ? player.matchId : ''
            }
          : player
      )
    );
    pushCommand(`Toggled ban for ${playerId}`);
  }

  function updateConfig(key, value) {
    setConfig((current) => ({ ...current, [key]: value }));
    if (config.realtime && key !== 'realtime') {
      pushCommand(`Applied ${key}: ${value}`);
    }
  }

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark"><Gamepad2 size={22} /></div>
          <div>
            <strong>Tank Legends</strong>
            <span>Game Ops Console</span>
          </div>
        </div>
        <nav className="nav-list">
          {[
            ['Players', Users],
            ['Store', Store],
            ['Leaderboard', Trophy],
            ['History', History],
            ['Servers', Server],
            ['Debug', TerminalSquare],
            ['Logs', Bell],
            ['Config', Gauge],
            ['Analytics', LineChart],
            ['Economy', Wallet],
            ['Roles', Shield]
          ].map(([label, Icon]) => (
            <a href={`#${label.toLowerCase()}`} key={label}>
              <Icon size={18} />
              <span>{label}</span>
            </a>
          ))}
        </nav>
        <div className="session-card">
          <span>Signed in</span>
          <strong>{session.username}</strong>
          <em>{session.role}</em>
        </div>
      </aside>

      <section className="workspace">
        <header className="topbar">
          <div>
            <p className="eyebrow">Asia shard / production</p>
            <h1>Tank Legends Management</h1>
          </div>
          <div className="topbar-actions">
            <span className={`role-pill ${session.role}`}>{session.role}</span>
            <span className="live-pill"><span /> Live telemetry</span>
            <button className="icon-button" aria-label="Refresh telemetry" onClick={() => pushCommand('Telemetry refreshed')}>
              <RefreshCcw size={18} />
            </button>
            <button onClick={() => setSession(null)}>Logout</button>
          </div>
        </header>

        <section className="metric-grid">
          <Metric icon={Server} label="Server" value="Online" sub="asia-main-01" tone="success" />
          <Metric icon={Cpu} label="CPU / RAM" value="42% / 68%" sub="basic monitor" tone="blue" />
          <Metric icon={Users} label="Registered" value={`${totalPlayers}`} sub="total players" tone="yellow" />
          <Metric icon={Activity} label="DAU" value="12.8k" sub="+8.4% today" tone="green" />
        </section>

        <div className="dashboard-grid">
          <section className="panel span-2" id="players">
            <PanelHeader icon={Users} title="Player Management" action="Data from Auth Service" />
            <div className="toolbar">
              <label className="search-box">
                <Search size={17} />
                <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search ID, username, email, role" />
              </label>
              <span className="count-chip">{filteredPlayers.length} players</span>
              <button onClick={fetchPlayers}><RefreshCcw size={16} /> Refresh</button>
            </div>
            <div className="table-wrap">
              {playersLoading ? (
                <p style={{ padding: '1rem' }}>Loading players from Auth Service...</p>
              ) : (
              <table className="player-admin-table">
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>Username</th>
                    <th>Email</th>
                    <th>Role</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredPlayers.map((player) => (
                    <tr key={player.playerId}>
                      <td><strong>{player.playerId}</strong></td>
                      <td><strong>{player.username}</strong></td>
                      <td>{player.email}</td>
                      <td>
                        <StatusBadge status={player.role === 'admin' ? 'banned' : player.role === 'user' ? 'online' : 'clear'} label={player.role} />
                      </td>
                      <td>
                        <div className="action-row">
                          <button title="View match history" onClick={() => { setHistoryPlayerId(String(player.rawId)); fetchPlayerHistory(String(player.rawId)); pushCommand(`Viewing history for ${player.username}`); }}>
                            <Eye size={16} /> History
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                  {filteredPlayers.length === 0 && (
                    <tr><td colSpan="5" style={{textAlign:'center', padding:'1rem'}}>{playersLoading ? 'Loading...' : 'No players found. Is Auth Service running?'}</td></tr>
                  )}
                </tbody>
              </table>
              )}
            </div>
          </section>

          <section className="panel span-2" id="store">
            <StoreManagementPanel pushCommand={pushCommand} />
          </section>

          <section className="panel span-2" id="leaderboard">
            <PanelHeader icon={Trophy} title="Leaderboard" action="Top players by kills" />
            <div className="toolbar">
              <button onClick={fetchLeaderboard}><RefreshCcw size={16} /> Refresh</button>
            </div>
            <div className="table-wrap">
              {leaderboardLoading ? (
                <p style={{ padding: '1rem' }}>Loading leaderboard...</p>
              ) : (
              <table className="player-admin-table">
                <thead>
                  <tr>
                    <th>Rank</th>
                    <th>Player ID</th>
                    <th>Total Kills</th>
                    <th>Total Matches</th>
                    <th>Wins</th>
                  </tr>
                </thead>
                <tbody>
                  {leaderboard.map((entry, idx) => (
                    <tr key={entry.playerId}>
                      <td><strong>#{idx + 1}</strong></td>
                      <td>
                        <button className="link-button" onClick={() => { setHistoryPlayerId(String(entry.playerId)); fetchPlayerHistory(String(entry.playerId)); }}>
                          {entry.playerId}
                        </button>
                      </td>
                      <td><strong>{entry.totalKills}</strong></td>
                      <td>{entry.totalMatches}</td>
                      <td>{entry.wins}</td>
                    </tr>
                  ))}
                  {leaderboard.length === 0 && (
                    <tr><td colSpan="5" style={{textAlign:'center', padding:'1rem'}}>No match data yet. Is History Service running?</td></tr>
                  )}
                </tbody>
              </table>
              )}
            </div>
          </section>

          <section className="panel span-2" id="history">
            <PanelHeader icon={History} title="Match History Lookup" action="Search by player ID" />
            <div className="toolbar">
              <label className="search-box">
                <Search size={17} />
                <input value={historyPlayerId} onChange={(e) => setHistoryPlayerId(e.target.value)} placeholder="Enter Player ID (e.g. 1, 2...)" />
              </label>
              <button className="primary" onClick={() => fetchPlayerHistory(historyPlayerId)}><Search size={16} /> Lookup</button>
            </div>

            {playerStats && (
              <div className="analytics-stack" style={{ marginBottom: '1rem' }}>
                <MiniStat label="Total Matches" value={playerStats.totalMatches} delta={`${(playerStats.winRate * 100).toFixed(1)}% win`} />
                <MiniStat label="W / L / D" value={`${playerStats.wins} / ${playerStats.losses} / ${playerStats.draws}`} delta="" />
                <MiniStat label="K / D" value={`${playerStats.totalKills} / ${playerStats.totalDeaths}`} delta={`Best: ${playerStats.bestKillStreak} kills`} />
              </div>
            )}

            <div className="table-wrap">
              {historyLoading ? (
                <p style={{ padding: '1rem' }}>Loading match history...</p>
              ) : (
              <table className="player-admin-table">
                <thead>
                  <tr>
                    <th>Match ID</th>
                    <th>Opponent</th>
                    <th>Result</th>
                    <th>K / D</th>
                    <th>Duration</th>
                    <th>Map</th>
                    <th>Played At</th>
                  </tr>
                </thead>
                <tbody>
                  {playerHistory.map((match) => (
                    <tr key={match.id}>
                      <td><strong>{match.matchId}</strong></td>
                      <td>{match.opponentId}</td>
                      <td>
                        <StatusBadge
                          status={match.result === 'WIN' ? 'online' : match.result === 'LOSE' ? 'banned' : 'clear'}
                          label={match.result}
                        />
                      </td>
                      <td><strong>{match.kills}</strong> / {match.deaths}</td>
                      <td>{Math.floor(match.durationSecs / 60)}m {match.durationSecs % 60}s</td>
                      <td>{match.mapName}</td>
                      <td>{match.playedAt ? new Date(match.playedAt).toLocaleString() : '-'}</td>
                    </tr>
                  ))}
                  {playerHistory.length === 0 && (
                    <tr><td colSpan="7" style={{textAlign:'center', padding:'1rem'}}>{historyPlayerId ? 'No matches found for this player.' : 'Enter a player ID and click Lookup.'}</td></tr>
                  )}
                </tbody>
              </table>
              )}
            </div>
          </section>

          <section className="panel" id="servers">
            <PanelHeader icon={Server} title="Server Monitoring" action="Realtime log stream" />
            <div className="server-card">
              <div>
                <span className="status-dot online" />
                <strong>asia-main-01</strong>
                <p>Ho Chi Minh edge / 31ms avg</p>
              </div>
              <Wifi size={28} />
            </div>
            <Meter label="CPU" value={42} />
            <Meter label="RAM" value={68} />
            <Meter label="Tick stability" value={91} />
            <div className="mini-log">
              {logsSeed.slice(0, 4).map((log) => <span key={`${log.time}-${log.message}`}>{log.time} {log.message}</span>)}
            </div>
          </section>

          <section className="panel span-2" id="debug">
            <PanelHeader icon={TerminalSquare} title="Match Control" action="Level 1 -> Level 2" />
            <div className="match-level-grid">
              <section className="match-level">
                <div className="level-heading">
                  <strong>Level 1: Match List</strong>
                  <span>Click vào match</span>
                </div>
                <div className="table-wrap compact-table">
                  <table>
                    <thead>
                      <tr>
                        <th>Match ID</th>
                        <th>Players</th>
                        <th>Status</th>
                        <th>Map</th>
                      </tr>
                    </thead>
                    <tbody>
                      {matchesSeed.map((match) => (
                        <tr
                          className={selectedMatchId === match.id ? 'selected-row' : ''}
                          key={match.id}
                          onClick={() => {
                            setSelectedMatchId(match.id);
                            setSelectedMap(match.map);
                          }}
                        >
                          <td><strong>{match.id}</strong><span>{match.name}</span></td>
                          <td>{match.players.length}/{match.maxPlayers}</td>
                          <td><StatusBadge status={match.status} /></td>
                          <td>{match.map}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </section>

              <section className="match-level">
                <div className="level-heading">
                  <strong>Level 2: Player trong match</strong>
                  <span>{selectedMatch.name} / {selectedMatch.id}</span>
                </div>
                <div className="table-wrap compact-table">
                  <table>
                    <thead>
                      <tr>
                        <th>ID</th>
                        <th>Pos</th>
                        <th>HP</th>
                        <th>State</th>
                        <th>Ping</th>
                      </tr>
                    </thead>
                    <tbody>
                      {selectedMatch.players.map((player) => (
                        <tr key={player.id} onClick={() => pushCommand(`Inspected ${player.id} in match ${selectedMatch.id}`)}>
                          <td><strong>{player.id}</strong></td>
                          <td>{player.pos}</td>
                          <td>
                            <div className="hp-cell">
                              <span>{player.hp}</span>
                              <progress value={player.hp} max="100" />
                            </div>
                          </td>
                          <td>{player.state}</td>
                          <td>{player.ping ? `${player.ping}ms` : '-'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <div className="control-stack">
                  <label>
                    Change map for selected match
                    <select value={selectedMap} onChange={(event) => setSelectedMap(event.target.value)}>
                      {maps.map((map) => <option value={map} key={map}>{map}</option>)}
                    </select>
                  </label>
                  <div className="command-grid">
                    <button onClick={() => pushCommand(`Spawned debug player in ${selectedMatch.id}`)}><UserPlus size={16} /> Spawn</button>
                    <button onClick={() => pushCommand(`Teleported squad in ${selectedMatch.id}`)}><Radar size={16} /> Teleport</button>
                    <button onClick={() => pushCommand(`Restarted ${selectedMatch.id}`)}><RefreshCcw size={16} /> Restart</button>
                    <button onClick={() => pushCommand(`${selectedMatch.id} map changed to ${selectedMap}`)}><Map size={16} /> Change map</button>
                  </div>
                </div>
              </section>
            </div>
            <div className="console" ref={consoleRef} onScroll={handleConsoleScroll}>
              {commandFeed.map((line, index) => <span key={`${line}-${index}`}>{line}</span>)}
            </div>
          </section>

          <section className="panel span-2" id="logs">
            <PanelHeader icon={Bell} title="Log Viewer" action="Filter error, warning, player ID" />
            <div className="toolbar">
              <div className="segmented">
                {['all', 'error', 'warning', 'info'].map((level) => (
                  <button className={logFilter === level ? 'active' : ''} onClick={() => setLogFilter(level)} key={level}>{level}</button>
                ))}
              </div>
              <label className="search-box compact">
                <Search size={16} />
                <input value={playerLogQuery} onChange={(event) => setPlayerLogQuery(event.target.value)} placeholder="Player ID" />
              </label>
            </div>
            <div className="log-list">
              {filteredLogs.map((log) => (
                <article className={`log-row ${log.level}`} key={`${log.time}-${log.message}`}>
                  <span>{log.time}</span>
                  <strong>{log.level}</strong>
                  <code>{log.playerId}</code>
                  <p>{log.message}</p>
                </article>
              ))}
            </div>
          </section>

          <section className="panel" id="config">
            <PanelHeader icon={Gauge} title="Config Management" action="Apply realtime" />
            <ConfigSlider label="Speed" value={config.speed} min={50} max={180} onChange={(value) => updateConfig('speed', value)} />
            <ConfigSlider label="Damage" value={config.damage} min={10} max={150} onChange={(value) => updateConfig('damage', value)} />
            <ConfigSlider label="Spawn rate" value={config.spawnRate} min={5} max={100} onChange={(value) => updateConfig('spawnRate', value)} />
            <label className="toggle">
              <input type="checkbox" checked={config.realtime} onChange={(event) => updateConfig('realtime', event.target.checked)} />
              <span>Realtime apply</span>
            </label>
            <button className="primary" onClick={() => pushCommand('Config applied to live shard')}><Bolt size={16} /> Apply config</button>
          </section>

          <section className="panel" id="analytics">
            <PanelHeader icon={LineChart} title="Analytics" action="Retention and sessions" />
            <div className="analytics-stack">
              <MiniStat label="DAU" value="12,840" delta="+8.4%" />
              <MiniStat label="D1 retention" value="46%" delta="+2.1%" />
              <MiniStat label="Avg session" value="18m 42s" delta="-0.4%" />
            </div>
            <div className="bar-chart" aria-label="Daily active user chart">
              {[44, 61, 53, 74, 68, 82, 91].map((height, index) => <span style={{ height: `${height}%` }} key={index} />)}
            </div>
          </section>

          <section className="panel" id="economy">
            <PanelHeader icon={Wallet} title="Economy" action="Currency and transactions" />
            <div className="economy-total">
              <Wallet size={24} />
              <div>
                <strong>8.4M coins</strong>
                <span>circulating currency</span>
              </div>
            </div>
            <div className="transaction-list">
              {transactions.map((transaction) => (
                <div key={transaction.id}>
                  <span>{transaction.id}</span>
                  <strong>{transaction.player}</strong>
                  <em>{transaction.amount}</em>
                </div>
              ))}
            </div>
          </section>

          <section className="panel" id="roles">
            <PanelHeader icon={Shield} title="Auth / Role" action="Admin, moderator, dev" />
            <div className="role-list">
              {roleProfiles.map((profile) => (
                <article key={profile.role} className={`role-card ${profile.color}`}>
                  <div>
                    <Shield size={18} />
                    <strong>{profile.role}</strong>
                  </div>
                  <p>{profile.access}</p>
                  <span>{profile.users} users</span>
                </article>
              ))}
            </div>
          </section>
        </div>
      </section>
    </main>
  );
}

function LoginScreen({ onLogin }) {
  const [username, setUsername] = useState('admin');
  const [password, setPassword] = useState('tanklegends');
  const [role, setRole] = useState('admin');

  function handleSubmit(event) {
    event.preventDefault();
    onLogin({
      username: username.trim() || 'operator',
      role,
      signedInAt: new Date().toISOString()
    });
  }

  return (
    <main className="login-shell">
      <section className="login-visual">
        <div className="login-brand">
          <div className="brand-mark"><Gamepad2 size={24} /></div>
          <div>
            <strong>Tank Legends</strong>
            <span>Management Web</span>
          </div>
        </div>
        <div className="tank-radar" aria-hidden="true">
          <span className="radar-sweep" />
          <span className="radar-dot one" />
          <span className="radar-dot two" />
          <span className="radar-dot three" />
          <Skull className="radar-icon" size={34} />
        </div>
      </section>

      <section className="login-panel">
        <form onSubmit={handleSubmit} className="login-form">
          <p className="eyebrow">Secure game ops</p>
          <h1>Login console</h1>
          <label>
            Username
            <input value={username} onChange={(event) => setUsername(event.target.value)} placeholder="your username" />
          </label>
          <label>
            Password
            <input value={password} onChange={(event) => setPassword(event.target.value)} placeholder="anything works for now" type="password" />
          </label>
          <div className="role-picker">
            <span>Choose role</span>
            <div>
              {['admin', 'moderator', 'dev'].map((item) => (
                <button
                  className={role === item ? 'active' : ''}
                  key={item}
                  onClick={() => setRole(item)}
                  type="button"
                >
                  <Shield size={16} />
                  {item}
                </button>
              ))}
            </div>
          </div>
          <button className="login-submit" type="submit">
            <Play size={17} />
            Enter dashboard
          </button>
        </form>
      </section>
    </main>
  );
}

function Metric({ icon: Icon, label, value, sub, tone }) {
  return (
    <article className={`metric-card ${tone}`}>
      <Icon size={22} />
      <div>
        <span>{label}</span>
        <strong>{value}</strong>
        <p>{sub}</p>
      </div>
    </article>
  );
}

function PanelHeader({ icon: Icon, title, action }) {
  return (
    <header className="panel-header">
      <div>
        <Icon size={19} />
        <h2>{title}</h2>
      </div>
      <span>{action}</span>
    </header>
  );
}

function StatusBadge({ status, label }) {
  return <span className={`status-badge ${status}`}>{label ?? status}</span>;
}

function Meter({ label, value }) {
  return (
    <div className="meter">
      <div><span>{label}</span><strong>{value}%</strong></div>
      <progress value={value} max="100" />
    </div>
  );
}

function ConfigSlider({ label, value, min, max, onChange }) {
  return (
    <label className="slider-row">
      <span>{label}<strong>{value}</strong></span>
      <input type="range" min={min} max={max} value={value} onChange={(event) => onChange(Number(event.target.value))} />
    </label>
  );
}

function MiniStat({ label, value, delta }) {
  return (
    <div className="mini-stat">
      <span>{label}</span>
      <strong>{value}</strong>
      <em>{delta}</em>
    </div>
  );
}

function StoreManagementPanel({ pushCommand }) {
  const [items, setItems] = useState([]);
  const [loading, setLoading] = useState(true);
  const [isEditing, setIsEditing] = useState(null);
  const [editForm, setEditForm] = useState({});

  const API_URL = 'http://localhost:8080/api/shop';

  const fetchItems = async () => {
    try {
      setLoading(true);
      const res = await fetch(`${API_URL}/items`);
      const data = await res.json();
      setItems(data);
    } catch (err) {
      console.error(err);
      pushCommand('Failed to fetch store items');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchItems();
  }, []);

  const handleToggleAvailable = async (item) => {
    try {
      const updatedItem = { ...item, available: !item.available };
      const res = await fetch(`${API_URL}/admin/items/${item.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(updatedItem)
      });
      if (res.ok) {
        setItems(items.map(i => i.id === item.id ? updatedItem : i));
        pushCommand(`Set ${item.name} availability to ${updatedItem.available}`);
      }
    } catch (err) {
      pushCommand(`Failed to update ${item.name}`);
    }
  };

  const handleSaveEdit = async () => {
    try {
      let res;
      if (editForm.id) {
        // Update
        res = await fetch(`${API_URL}/admin/items/${editForm.id}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(editForm)
        });
      } else {
        // Create
        res = await fetch(`${API_URL}/admin/items`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(editForm)
        });
      }
      
      if (res.ok) {
        fetchItems();
        setIsEditing(null);
        pushCommand(`Saved store item: ${editForm.name}`);
      }
    } catch (err) {
      pushCommand('Failed to save store item');
    }
  };

  const handleDelete = async (id, name) => {
    if (!confirm('Are you sure you want to delete this item completely?')) return;
    try {
      const res = await fetch(`${API_URL}/admin/items/${id}`, {
        method: 'DELETE'
      });
      if (res.ok) {
        fetchItems();
        pushCommand(`Deleted store item: ${name}`);
      }
    } catch (err) {
      pushCommand('Failed to delete store item');
    }
  };

  return (
    <>
      <PanelHeader icon={Store} title="Store Management" action="Add / Edit / Remove Tanks" />
      <div className="toolbar">
        <button className="primary" onClick={() => { setEditForm({ name: '', description: '', price: 0, category: 'Tank', available: true, imageUrl: '' }); setIsEditing('new'); }}>
          <Store size={16} /> Add New Item
        </button>
        <button onClick={fetchItems}><RefreshCcw size={16} /> Refresh</button>
      </div>

      {isEditing && (
        <div className="store-edit-form">
          <div className="form-grid">
            <label>Name <input value={editForm.name} onChange={e => setEditForm({...editForm, name: e.target.value})} /></label>
            <label>Category <input value={editForm.category} onChange={e => setEditForm({...editForm, category: e.target.value})} /></label>
            <label>Price <input type="number" value={editForm.price} onChange={e => setEditForm({...editForm, price: parseFloat(e.target.value)})} /></label>
            <label>Image URL <input value={editForm.imageUrl} onChange={e => setEditForm({...editForm, imageUrl: e.target.value})} /></label>
            <label className="span-2">Description <input value={editForm.description} onChange={e => setEditForm({...editForm, description: e.target.value})} /></label>
            <label className="checkbox-label" style={{ gridColumn: 'span 2', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <input type="checkbox" checked={editForm.available} onChange={e => setEditForm({...editForm, available: e.target.checked})} style={{ width: 'auto' }} />
              Available in Store
            </label>
          </div>
          <div className="form-actions" style={{ display: 'flex', gap: '0.5rem', marginTop: '1rem' }}>
            <button className="primary" onClick={handleSaveEdit}>Save</button>
            <button onClick={() => setIsEditing(null)}>Cancel</button>
          </div>
        </div>
      )}

      <div className="table-wrap">
        {loading ? (
          <p style={{ padding: '1rem' }}>Loading store items...</p>
        ) : (
          <table className="store-admin-table player-admin-table">
            <thead>
              <tr>
                <th>ID</th>
                <th>Item Info</th>
                <th>Category</th>
                <th>Price</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {items.map(item => (
                <tr key={item.id} className={!item.available ? 'inactive-row' : ''} style={{ opacity: item.available ? 1 : 0.6 }}>
                  <td><strong>{item.id}</strong></td>
                  <td>
                    <strong>{item.name}</strong>
                    <span style={{display:'block', fontSize:'0.85em', color:'var(--text-dim)'}}>{item.description}</span>
                  </td>
                  <td>{item.category}</td>
                  <td><strong>{item.price}</strong></td>
                  <td>
                    <StatusBadge status={item.available ? 'online' : 'offline'} label={item.status || (item.available ? 'On Sale' : 'Discontinued')} />
                  </td>
                  <td>
                    <div className="action-row">
                      <button onClick={() => handleToggleAvailable(item)}>
                        {item.available ? 'Hide' : 'Show'}
                      </button>
                      <button onClick={() => { setEditForm(item); setIsEditing(item.id); }}>
                        Edit
                      </button>
                      <button className="danger" onClick={() => handleDelete(item.id, item.name)}>
                        <XCircle size={16}/> Delete
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
              {items.length === 0 && (
                <tr><td colSpan="6" style={{textAlign:'center', padding:'1rem'}}>No items found.</td></tr>
              )}
            </tbody>
          </table>
        )}
      </div>
    </>
  );
}

createRoot(document.getElementById('root')).render(<App />);
