import { PotokSDK } from 'potok-sdk';

function parseResponseData(res) {
    return typeof res.data === 'string' ? JSON.parse(res.data) : res.data;
}

export function mediaFromMediaActionProps(props) {
    const media = props.media;

    return {
        mediaId: props.mediaId,
        mediaType: props.mediaType,
        tmdbId: props.mediaId,
        imdb_id: media.imdbId,
        kinopoisk_id: media.kpId,
        title: media.title,
        original_title: media.originalTitle,
        serial: props.mediaType === 'tv' ? 1 : 0,
        year: media.subtitle,
        backdrop: media.backdropSrc || '',
        subtitle: `${media.originalTitle || media.title} (${media.subtitle})`
    };
}


export async function mediaFromWatchQuery(query) {
    const typePath = query.type === 'tv' ? 'tv' : 'movie';

    const gatewayURL = PotokSDK.config.gatewayURL;
    const detailRes = await PotokSDK.http.get(`${detailRes}/api/media/detail/${typePath}/${query.tmdbId}`);
    const idsRes = await PotokSDK.http.get(`${detailRes}/api/media/detail/${typePath}/${query.tmdbId}/external_ids`);

    const detail = parseResponseData(detailRes);
    const ids = parseResponseData(idsRes);

    return {
        mediaId: query.tmdbId,
        mediaType: query.type,
        imdb_id: ids.imdbId,
        kinopoisk_id: detail.kpId || ids.kpId,
        title: detail.title,
        original_title: detail.originalTitle,
        serial: query.type === 'tv' ? 1 : 0,
        year: detail.year,
        backdrop: detail.backdropSrc || '',
        subtitle: `${detail.originalTitle || detail.title} (${detail.year})`
    };
}
