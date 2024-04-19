const ws = new WebSocket('ws://' + window.location.hostname + ':' + window.location.port);
let conn_err;
let content;
let audio_player;
let video_player;
let text;
let audio_should_play = false;
let video_should_play = false;

function loaded() {
  conn_err = document.getElementById('conn_err');
  content = document.getElementById('content');
  audio_player = document.createElement('audio');
  video_player = document.createElement('video');
  video_player.style.position = 'absolute';
  video_player.addEventListener('ended', (event) => {
    clear_video();
    // If the video ended also clear text?
    clear_text();
  });
  video_player.addEventListener('loadedmetadata', (event) => {
    update_video_player();
  });
  video_player.addEventListener('error', (event) => {
    clear_video();
  });
  video_player.style.transform = 'translate(-50%, -50%)';
  video_player.hidden = true;
  text = document.createElement('h1');

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
    </style>`;
}

window.addEventListener('load', loaded);

ws.addEventListener('open', () => {
  console.log('WebSocket connection established!');
  conn_err.hidden = true;
  content.hidden = false;
})

ws.addEventListener('close', () => {
  console.log('WebSocket connection closed!');
  conn_err.hidden = false;
  content.hidden = true;
  clear_content();
});

ws.addEventListener('message', (e) => {
  let data = JSON.parse(e.data);
  // console.log(data);

  // Clear all of the content
  if (data.type == 'clear_all') {
    clear_content();
  } else if (data.type == 'clear_video') {
    clear_video();
  } else if (data.type == 'clear_audio') {
    clear_audio();
  } else if (data.type == 'clear_text') {
    clear_text();
  } else if (data.type == 'pause') {
    pause();
  } else if (data.type == 'resume') {
    resume();
  } else {
    video_should_play = false;
    audio_should_play = false;

    // Play video
    if (data.video?.length > 0) {
      play_video(data);
    }

    // Play audio
    if (data.audio?.length > 0) {
      play_audio(data);
    }

    // Display text
    if (data.text?.length > 0) {
      display_text(data);
    }
  }

  ws.send('message_parsed');
});

function clear_content() {
  clear_audio();
  clear_video();

  content.childNodes.forEach((element) => {
    content.removeChild(element);
  });
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
  text.childNodes.forEach((element) => {
    text.removeChild(element);
  })
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
  data.text.split("\r\n").forEach((element) => {
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
  audio_player.src = data.audio;
  audio_player.play();
}

function play_video(data) {
  video_should_play = true;

  video_player.hidden = false;
  video_player.pause();
  video_player.volume = data.video_volume;
  video_player.src = data.video;

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
  content.appendChild(video_player);
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
