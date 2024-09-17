let ws; // WebSocket connection
let conn_err; // Div containing elements that should be displayed on WebSocket connection error
let content; // Div for elements to be displayed with notifications
let audio_player; // Audio player
let video_player; // Video player
let text; // Notification text
let audio_should_play = false; // Should audio be played?
let video_should_play = false; // Shoud video be played?
let gamba_player, gamba_text_name, gamba_text_value; // Gamba elements (video player, text, other text)
let static_url = 'http://127.0.0.1:40000/'; // Static default address
let static_ws = 'ws://127.0.0.1:40000/';

function loaded() {
  conn_err = document.getElementById('conn_err');
  content = document.getElementById('content');

  audio_player = document.createElement('audio');
  audio_player.addEventListener('ended', (event) => {
    clear_audio();
    ws.send('audio_end');
  });

  video_player = document.createElement('video');
  video_player.style.position = 'absolute';
  video_player.style.transform = 'translate(-50%, -50%)';
  video_player.hidden = true;
  video_player.addEventListener('ended', (event) => {
    clear_video();
    clear_text();
    ws.send('video_end');
  });
  video_player.addEventListener('loadedmetadata', (event) => {
    update_video_player();
  });
  video_player.addEventListener('error', (event) => {
    clear_video();
  });
  document.body.appendChild(video_player);

  text = document.createElement('h1');

  gamba_player = document.createElement('video');
  gamba_player.style.position = 'absolute';
  gamba_player.style.transform = 'translate(-50%, -100%)';
  gamba_player.style.left = '50%';
  gamba_player.style.top = '100%';
  gamba_player.height = 200;
  gamba_player.hidden = true;
  gamba_player.addEventListener('ended', (event) => {
    clear_gamba();
  });
  gamba_player.addEventListener('error', (event) => {
    clear_gamba();
  });
  gamba_text_name = document.createElement('h2');
  gamba_text_name.style.transform = 'translate(-50%, -100%)';
  gamba_text_name.style.left = '50%';
  gamba_text_name.style.top = (document.documentElement.clientHeight - gamba_player.height) + 'px';
  gamba_text_name.appendChild(document.createTextNode(''));
  gamba_text_name.hidden = true;
  gamba_text_value = document.createElement('h2');
  gamba_text_value.style.transform = 'translate(-50%, 0%)';
  gamba_text_value.style.left = '50%';
  gamba_text_value.style.top = (document.documentElement.clientHeight - gamba_player.height) + 'px';
  gamba_text_value.appendChild(document.createTextNode(''));
  gamba_text_value.hidden = true;
  document.body.appendChild(gamba_player);
  document.body.appendChild(gamba_text_name);
  document.body.appendChild(gamba_text_value);

  document.head.innerHTML += `
    <style>
      h1 {
        color: deepskyblue;
        font-size: 72px;
        font-family: Calibri;
        -webkit-text-stroke: 1px black;
        margin: 0;
        position: absolute;
        text-align: center;
        text-wrap: nowrap;
      }

      h2 {
        color: gold;
        font-size: 24px;
        font-family: Calibri;
        -webkit-text-stroke: 0.5px black;
        margin: 0;
        position: absolute;
        text-align: center;
        text-wrap: nowrap;
      }
    </style>`;

  // For html received from the server reset static url and ws
  if (typeof fromServer !== 'undefined') {
    static_url = '';
    static_ws = '';
  }

  connect();

  setInterval(function () {
    // try to reconnect every 5 sec
    if (ws.readyState != 1) {
      if (ws != null && ws.readyState == 0) {
        ws.close();
      }
      connect();
    }
  }, 5000);
}

window.addEventListener('load', loaded);

function connect() {
  if (static_ws.length > 0) {
    ws = new WebSocket(static_ws);
  } else {
    ws = new WebSocket('ws://' + window.location.hostname + ':' + window.location.port);
  }

  ws.addEventListener('open', () => {
    console.log('WebSocket connection established!');
    conn_err.hidden = true;
    content.hidden = false;
  });

  ws.addEventListener('close', () => {
    console.log('WebSocket connection closed!');
    conn_err.hidden = false;
    content.hidden = true;
    clear_content();
  });

  ws.addEventListener('message', (e) => {
    parse_message(e.data);
  });

  ws.addEventListener('error', (err) => {
    console.error('Socket encountered error: ', err.message);
    ws.close();
  });
}

function parse_message(d) {
  let data = JSON.parse(d);
  // console.log(data);

  switch (data.type) {
    case "clear_all":
      clear_content();
      break;
    case "clear_video":
      clear_video();
      break;
    case "clear_audio":
      clear_audio();
      break;
    case "clear_text":
      clear_text();
      break;
    case "pause":
      pause();
      break;
    case "resume":
      resume();
      break;
    case "play_video":
      if (data.video?.length > 0) {
        play_video(data);
      }
      break;
    case "play_audio":
      if (data.audio?.length > 0) {
        play_audio(data);
      }
      break;
    case "display_text":
      if (data.text?.length > 0) {
        display_text(data);
      }
      break;
    case "gamba_animation":
      if (data.gamba?.length > 0) {
        play_gamba(data);
      }
      break;
    default:
      console.log('data type not recognized: ' + data.type);
      break;
  }

  ws.send('message_parsed');
}

function clear_content() {
  clear_audio();
  clear_video();
  clear_text();

  content.innerHTML = '';
}

function clear_video() {
  video_should_play = false;
  video_player.hidden = true;
  video_player.pause();
  video_player.src = '';
  video_player.removeAttribute('src');
}

function clear_audio() {
  audio_should_play = false;
  audio_player.pause();
  audio_player.src = '';
  audio_player.removeAttribute('src');
}

function clear_text() {
  text.innerHTML = '';
  if (content.contains(text)) {
    content.removeChild(text);
  }
  // Reset position and transform
  text.style.left = '0%';
  text.style.top = '0%';
  text.style.transform = 'translate(0%, 0%)';
}

function pause() {
  video_player.pause();
  video_player.hidden = true;

  audio_player.pause();
}

function resume() {
  if (video_should_play) {
    video_player.hidden = false;
    video_player.play();
  }

  if (audio_should_play) {
    audio_player.play();
  }
}

function display_text(data) {
  let addBr = false;
  data.text.split('\r\n').forEach((element) => {
    if (addBr) {
      text.appendChild(document.createElement('br'));
    }
    text.appendChild(document.createTextNode(element));
    addBr = true;
  });

  if (data.text_size > 0) {
    text.style.fontSize = data.text_size + 'px';
  }
  switch (data.text_position) {
    case 'TOPLEFT':
      // no change needed
      break;
    case 'TOP':
      text.style.left = '50%';
      text.style.transform = 'translateX(-50%)';
      break;
    case 'TOPRIGHT':
      text.style.left = '100%';
      text.style.transform = 'translateX(-100%)';
      break;
    case 'LEFT':
      text.style.top = '50%';
      text.style.transform = 'translateY(-50%)';
      break;
    case 'CENTER':
      text.style.left = '50%';
      text.style.top = '50%';
      text.style.transform = 'translate(-50%, -50%)';
      break;
    case 'RIGHT':
      text.style.left = '100%';
      text.style.top = '50%';
      text.style.transform = 'translate(-100%, -50%)';
      break;
    case 'BOTTOMLEFT':
      text.style.top = '100%';
      text.style.transform = 'translateY(-100%)';
      break;
    case 'BOTTOM':
      text.style.left = '50%';
      text.style.top = '100%';
      text.style.transform = 'translate(-50%, -100%)';
      break;
    case 'BOTTOMRIGHT':
      text.style.left = '100%';
      text.style.top = '100%';
      text.style.transform = 'translate(-100%, -100%)';
      break;
    case 'VIDEOABOVE':
      text.style.left = video_player.style.left;
      text.style.top = (parseInt(video_player.style.top.slice(0, -2), 10) - video_player.height / 2) + 'px';
      text.style.transform = 'translate(-50%, -100%)';
      break;
    case 'VIDEOCENTER':
      text.style.left = video_player.style.left;
      text.style.top = video_player.style.top;
      text.style.transform = 'translate(-50%, -50%)';
      break;
    case 'VIDEOBELOW':
      text.style.left = video_player.style.left;
      text.style.top = (parseInt(video_player.style.top.slice(0, -2), 10) + video_player.height / 2) + 'px';
      text.style.transform = 'translateX(-50%)';
      break;
  }
  content.appendChild(text);
}

function play_audio(data) {
  audio_should_play = true;

  audio_player.pause();
  audio_player.volume = data.audio_volume;
  audio_player.src = static_url + data.audio;
  audio_player.play();
}

function play_video(data) {
  video_should_play = true;

  video_player.hidden = false;
  video_player.pause();
  video_player.volume = data.video_volume;
  if (data.video.startsWith('http')) {
    video_player.src = data.video;
  } else {
    video_player.src = static_url + data.video;
  }

  // Set video position
  if (data.video_position[0] >= 0) {
    video_player.style.left = data.video_position[0] + 'px';
  } else {
    video_player.style.left = '50%';
  }
  if (data.video_position[1] >= 0) {
    video_player.style.top = data.video_position[1] + 'px';
  } else {
    video_player.style.top = '50%';
  }

  // Set video size
  if (data.video_size[0] >= 0) {
    video_player.width = data.video_size[0];
  } else {
    video_player.width = 0;
    video_player.removeAttribute('width');
  }
  if (data.video_size[1] >= 0) {
    video_player.height = data.video_size[1];
  } else {
    video_player.height = 0;
    video_player.removeAttribute('height');
  }

  video_player.play();
}

function play_gamba(data) {
  gamba_player.hidden = false;
  gamba_player.src = static_url + data.gamba;
  gamba_player.play();

  gamba_text_name.childNodes[0].textContent = data.gamba_name;
  gamba_text_name.hidden = false;
  gamba_text_value.childNodes[0].textContent = data.gamba_points_rolled;
  gamba_text_value.hidden = false;
}

function clear_gamba() {
  gamba_player.hidden = true;
  gamba_player.pause();
  gamba_player.src = '';
  gamba_player.removeAttribute('src');

  gamba_text_name.childNodes[0].textContent = '';
  gamba_text_name.hidden = true;
  gamba_text_value.childNodes[0].textContent = '';
  gamba_text_value.hidden = true;
}

function update_video_player() {
  if (video_player.width == 0 || video_player.height == 0) {
    video_player.width = video_player.videoWidth;
    video_player.height = video_player.videoHeight;
  } else {
    // Keeps the aspect ratio of original video
    let w = video_player.videoWidth;
    let h = video_player.videoHeight;
    let aspect = h / w;
    let new_h = video_player.width * aspect;
    if (new_h <= video_player.height) {
      video_player.height = video_player.width * aspect;
    } else {
      video_player.width = video_player.height / aspect;
    }
  }
}
