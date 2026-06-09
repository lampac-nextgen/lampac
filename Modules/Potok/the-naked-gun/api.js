import { PotokSDK } from 'potok-sdk';

function parseResponseData(res) {
    return typeof res.data === 'string' ? JSON.parse(res.data) : res.data;
}

export async function getJson(url) {
    const res = await PotokSDK.http.get(url);
    return parseResponseData(res);
}

export function buildMediaParams(media) {
    const params = new URLSearchParams();

    params.set('life', 'false');
    params.set('imdb_id', media.imdb_id);
    params.set('kinopoisk_id', String(media.kinopoisk_id));
    params.set('title', media.title);
    params.set('original_title', media.original_title);
    params.set('serial', String(media.serial));
    params.set('year', String(media.year));
    params.set('rjson', 'true');

    return params;
}

export async function fetchEvents(media) {
    const url = new URL('{localhost}/lite/events');
    url.search = buildMediaParams(media).toString();
    return getJson(url.toString() + '&initial=potok');
}

export async function fetchSourceJson(sourceUrl, media) {
    const url = new URL(sourceUrl);
    const params = buildMediaParams(media);

    params.forEach((value, key) => {
        url.searchParams.set(key, value);
    });

    const json = await getJson(url.toString() + '&initial=potok');

    if (!json.type) {
        throw new Error('Invalid source response');
    }

    return json;
}

export async function fetchUrlJson(url) {
    const json = await getJson(url + '&initial=potok');

    if (!json.type) {
        throw new Error('Invalid nested response');
    }

    return json;
}
