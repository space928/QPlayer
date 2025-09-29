// This class based on the Resampler that is part of Cockos WDL
// originally written in C++ and ported to C# for NAudio by Mark Heath
// further improvements by Thomas Mathieson
// Used in NAudio with permission from Justin Frankel
// Original WDL License:
//     Copyright (C) 2005 and later Cockos Incorporated
//     
//     Portions copyright other contributors, see each source file for more information
// 
//     This software is provided 'as-is', without any express or implied
//     warranty.  In no event will the authors be held liable for any damages
//     arising from the use of this software.
// 
//     Permission is granted to anyone to use this software for any purpose,
//     including commercial applications, and to alter it and redistribute it
//     freely, subject to the following restrictions:
// 
//     1. The origin of this software must not be misrepresented; you must not
//        claim that you wrote the original software. If you use this software
//        in a product, an acknowledgment in the product documentation would be
//        appreciated but is not required.
//     2. Altered source versions must be plainly marked as such, and must not be
//        misrepresented as being the original software.
//     3. This notice may not be removed or altered from any source distribution.


using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Channels;

namespace QPlayer.Audio;

/// <summary>
/// Fully managed resampler, based on Cockos WDL Resampler
/// </summary>
public class WdlResampler
{
    private const int WDL_RESAMPLE_MAX_FILTERS = 4;
    private const int WDL_RESAMPLE_MAX_NCH = 64;
    private const double PI = 3.1415926535897932384626433832795;

    private readonly double m_sratein; // WDL_FIXALIGN
    private readonly double m_srateout;
    private double m_fracpos;
    private readonly double m_ratio;
    private double m_filter_ratio;
    private readonly float m_filterq, m_filterpos;
    private float[] m_rsinbuf; // WDL_TypedBuf<float>
    private float[] m_filter_coeffs; // WDL_TypedBuf<float>

    private WDLResampler_IIRFilter? m_iirfilter; // WDL_Resampler_IIRFilter *

    private int m_filter_coeffs_size;
    private int m_last_requested;
    private int m_filtlatency;
    private int m_samples_in_rsinbuf;
    private int m_lp_oversize;

    private readonly int m_sincsize;
    private readonly int m_filtercnt;
    private readonly int m_sincoversize;
    private readonly bool m_interp;
    private readonly bool m_feedmode;

    /// <summary>
    /// Creates a new Resampler
    /// </summary>
    public WdlResampler(double rate_in, double rate_out,
        bool interp, int filtercnt, bool sinc, int sinc_size = 64, int sinc_interpsize = 32,
        float filterpos = 0.693f, float filterq = 0.707f)
    {
        m_filterq = filterq;
        m_filterpos = filterpos; // .792 ?

        m_lp_oversize = 1;
        m_feedmode = false;

        m_sratein = 44100.0;
        m_srateout = 44100.0;
        m_ratio = 1.0;
        m_filter_ratio = -1.0;

        m_rsinbuf = [];
        m_filter_coeffs = [];

        m_sincsize = sinc && sinc_size >= 4 ? sinc_size > 8192 ? 8192 : sinc_size : 0;
        m_sincoversize = (m_sincsize != 0) ? (sinc_interpsize <= 1 ? 1 : sinc_interpsize >= 4096 ? 4096 : sinc_interpsize) : 1;

        m_filtercnt = (m_sincsize != 0) ? 0 : (filtercnt <= 0 ? 0 : filtercnt >= WDL_RESAMPLE_MAX_FILTERS ? WDL_RESAMPLE_MAX_FILTERS : filtercnt);
        m_interp = interp && (m_sincsize == 0);

        //Debug.WriteLine(String.Format("setting interp={0}, filtercnt={1}, sinc={2},{3}\n", m_interp, m_filtercnt, m_sincsize, m_sincoversize));

        if (m_sincsize == 0)
        {
            m_filter_coeffs = []; //.Resize(0);
            m_filter_coeffs_size = 0;
        }
        if (m_filtercnt == 0)
        {
            m_iirfilter = null;
        }

        m_feedmode = false;

        if (rate_in < 1.0) rate_in = 1.0;
        if (rate_out < 1.0) rate_out = 1.0;
        if (rate_in != m_sratein || rate_out != m_srateout)
        {
            m_sratein = rate_in;
            m_srateout = rate_out;
            m_ratio = m_sratein / m_srateout;
        }

        Reset();
    }

    /// <summary>
    /// Reset
    /// </summary>
    public void Reset(double fracpos = 0.0)
    {
        m_last_requested = 0;
        m_filtlatency = 0;
        m_fracpos = fracpos;
        m_samples_in_rsinbuf = 0;
        m_iirfilter?.Reset();
    }

    /// <summary>
    /// amount of input that has been received but not yet converted to output, in seconds
    /// </summary>
    public double GetCurrentLatency()
    {
        double v = ((double)m_samples_in_rsinbuf - m_filtlatency) / m_sratein;

        if (v < 0.0) v = 0.0;
        return v;
    }

    /// <summary>
    /// Prepare
    /// note that it is safe to call ResamplePrepare without calling ResampleOut (the next call of ResamplePrepare will function as normal)
    /// nb inbuffer was float **, returning a place to put the in buffer, so we return a buffer and offset
    /// </summary>
    /// <param name="out_samples">req_samples is output samples desired if !wantInputDriven, or if wantInputDriven is input samples that we have</param>
    /// <param name="nch"></param>
    /// <param name="inbuffer"></param>
    /// <param name="inbufferOffset"></param>
    /// <returns>returns number of samples desired (put these into *inbuffer)</returns>
    public int ResamplePrepare(int out_samples, int nch, out float[] inbuffer, out int inbufferOffset)
    {
        if (nch > WDL_RESAMPLE_MAX_NCH || nch < 1)
        {
            inbuffer = [];
            inbufferOffset = 0;
            return 0;
        }

        int fsize = 0;
        if (m_sincsize > 1)
            fsize = m_sincsize;

        int hfs = fsize / 2;
        if (hfs > 1 && m_samples_in_rsinbuf < hfs - 1)
        {
            m_filtlatency += hfs - 1 - m_samples_in_rsinbuf;

            m_samples_in_rsinbuf = hfs - 1;

            if (m_samples_in_rsinbuf > 0)
                m_rsinbuf = new float[m_samples_in_rsinbuf * nch];
        }

        int sreq;
        if (!m_feedmode)
            sreq = (int)(m_ratio * out_samples) + 4 + fsize - m_samples_in_rsinbuf;
        else
            sreq = out_samples;

        if (sreq < 0)
            sreq = 0;

        while (true)
        {
            int buffSize = (m_samples_in_rsinbuf + sreq) * nch;
            int sz = buffSize / nch - m_samples_in_rsinbuf;
            if (sz != sreq)
            {
                if (sreq > 4 && (sz == 0))
                {
                    sreq /= 2;
                    continue; // try again with half the size
                }
                sreq = sz;
            }
            if (buffSize > m_rsinbuf.Length)
                Array.Resize(ref m_rsinbuf, buffSize);
            break;
        }

        inbuffer = m_rsinbuf;
        inbufferOffset = m_samples_in_rsinbuf * nch;

        m_last_requested = sreq;
        return sreq;
    }

    /// <summary>
    /// if numsamples_in &lt; the value return by ResamplePrepare(), then it will be flushed to produce all remaining valid samples
    /// do NOT call with nsamples_in greater than the value returned from resamplerprpare()! the extra samples will be ignored.
    /// returns number of samples successfully outputted to out
    /// </summary>
    public int ResampleOut(float[] outBuffer, int outBufferIndex, int nsamples_in, int nsamples_out, int nch)
    {
        if (nch > WDL_RESAMPLE_MAX_NCH || nch < 1)
        {
            return 0;
        }

        if (m_filtercnt > 0)
        {
            if (m_ratio > 1.0 && nsamples_in > 0) // filter input
            {
                m_iirfilter ??= new WDLResampler_IIRFilter();

                int n = m_filtercnt;
                m_iirfilter.SetParms((float)((1 / m_ratio) * m_filterpos), m_filterq);

                int bufIndex = m_samples_in_rsinbuf * nch;
                int a, x;
                int offs = 0;
                if (nch == 2)
                {
                    // Fast path for stereo
                    for (a = 0; a < n; a++)
                        m_iirfilter.Apply(m_rsinbuf, bufIndex, nsamples_in, 2, offs++);
                }
                else
                {
                    for (x = 0; x < nch; x++)
                        for (a = 0; a < n; a++)
                            m_iirfilter.Apply(m_rsinbuf, bufIndex + x, nsamples_in, 1, offs++);
                }
            }
        }

        m_samples_in_rsinbuf += Math.Min(nsamples_in, m_last_requested); // prevent the user from corrupting the internal state


        int rsinbuf_availtemp = m_samples_in_rsinbuf;

        if (nsamples_in < m_last_requested) // flush out to ensure we can deliver
        {
            int fsize = (m_last_requested - nsamples_in) * 2 + m_sincsize * 2;

            int alloc_size = (m_samples_in_rsinbuf + fsize) * nch;
            if (alloc_size > m_rsinbuf.Length)
                Array.Resize(ref m_rsinbuf, alloc_size);
            int samplesIn = m_samples_in_rsinbuf * nch;
            m_rsinbuf.AsSpan(samplesIn, alloc_size - samplesIn).Clear();
            rsinbuf_availtemp = m_samples_in_rsinbuf + fsize;
        }

        int ret = 0;
        double srcpos = m_fracpos;
        double drspos = m_ratio;
        int localin = 0; // localin is an index into m_rsinbuf

        int outptr = outBufferIndex;  // outptr is an index into  outBuffer;

        int ns = nsamples_out;

        int outlatadj = 0;

        if (m_sincsize != 0) // sinc interpolating
        {
            if (m_ratio > 1.0)
                BuildLowPass(1.0 / (m_ratio * 1.03));
            else
                BuildLowPass(1.0);

            int filtsz = m_filter_coeffs_size;
            int filtlen = rsinbuf_availtemp - filtsz;
            outlatadj = filtsz / 2 - 1;
            int filter = 0; // filter is an index into m_filter_coeffs m_filter_coeffs.Get();

            if (nch == 1)
            {
                while (ns-- != 0)
                {
                    int ipos = (int)srcpos;

                    if (ipos >= filtlen - 1) break; // quit decoding, not enough input samples

                    SincSample1(outBuffer, outptr, m_rsinbuf, localin + ipos, srcpos - ipos, m_filter_coeffs, filter, filtsz);
                    outptr++;
                    srcpos += drspos;
                    ret++;
                }
            }
            else if (nch == 2)
            {
                while (ns-- != 0)
                {
                    int ipos = (int)srcpos;

                    if (ipos >= filtlen - 1) break; // quit decoding, not enough input samples

                    SincSample2(outBuffer, outptr, m_rsinbuf, localin + ipos * 2, srcpos - ipos, m_filter_coeffs, filter, filtsz);
                    outptr += 2;
                    srcpos += drspos;
                    ret++;
                }
            }
            else
            {
                while (ns-- != 0)
                {
                    int ipos = (int)srcpos;

                    if (ipos >= filtlen - 1) break; // quit decoding, not enough input samples

                    SincSample(outBuffer, outptr, m_rsinbuf, localin + ipos * nch, srcpos - ipos, nch, m_filter_coeffs, filter, filtsz);
                    outptr += nch;
                    srcpos += drspos;
                    ret++;
                }
            }
        }
        else if (!m_interp) // point sampling
        {
            if (nch == 1)
            {
                while (ns-- != 0)
                {
                    int ipos = (int)srcpos;
                    if (ipos >= rsinbuf_availtemp) break; // quit decoding, not enough input samples

                    outBuffer[outptr++] = m_rsinbuf[localin + ipos];
                    srcpos += drspos;
                    ret++;
                }
            }
            else if (nch == 2)
            {
                while (ns-- != 0)
                {
                    int ipos = (int)srcpos;
                    if (ipos >= rsinbuf_availtemp) break; // quit decoding, not enough input samples

                    ipos += ipos;

                    outBuffer[outptr + 0] = m_rsinbuf[localin + ipos];
                    outBuffer[outptr + 1] = m_rsinbuf[localin + ipos + 1];
                    outptr += 2;
                    srcpos += drspos;
                    ret++;
                }
            }
            else
                while (ns-- != 0)
                {
                    int ipos = (int)srcpos;
                    if (ipos >= rsinbuf_availtemp) break; // quit decoding, not enough input samples

                    Array.Copy(m_rsinbuf, localin + ipos * nch, outBuffer, outptr, nch);
                    outptr += nch;
                    srcpos += drspos;
                    ret++;
                }
        }
        else // linear interpolation
        {
            if (nch == 1)
            {
                while (ns-- != 0)
                {
                    int ipos = (int)srcpos;
                    double fracpos = srcpos - ipos;

                    if (ipos >= rsinbuf_availtemp - 1)
                    {
                        break; // quit decoding, not enough input samples
                    }

                    double ifracpos = 1.0 - fracpos;
                    int inptr = localin + ipos;
                    outBuffer[outptr++] = (float)(m_rsinbuf[inptr] * (ifracpos) + m_rsinbuf[inptr + 1] * (fracpos));
                    srcpos += drspos;
                    ret++;
                }
            }
            else if (nch == 2)
            {
                while (ns-- != 0)
                {
                    int ipos = (int)srcpos;
                    double fracpos = srcpos - ipos;

                    if (ipos >= rsinbuf_availtemp - 1)
                    {
                        break; // quit decoding, not enough input samples
                    }

                    double ifracpos = 1.0 - fracpos;
                    int inptr = localin + ipos * 2;
                    outBuffer[outptr + 0] = (float)(m_rsinbuf[inptr] * (ifracpos) + m_rsinbuf[inptr + 2] * (fracpos));
                    outBuffer[outptr + 1] = (float)(m_rsinbuf[inptr + 1] * (ifracpos) + m_rsinbuf[inptr + 3] * (fracpos));
                    outptr += 2;
                    srcpos += drspos;
                    ret++;
                }
            }
            else
            {
                while (ns-- != 0)
                {
                    int ipos = (int)srcpos;
                    double fracpos = srcpos - ipos;

                    if (ipos >= rsinbuf_availtemp - 1)
                    {
                        break; // quit decoding, not enough input samples
                    }

                    double ifracpos = 1.0 - fracpos;
                    int ch = nch;
                    int inptr = localin + ipos * nch;
                    while (ch-- != 0)
                    {
                        outBuffer[outptr++] = (float)(m_rsinbuf[inptr] * (ifracpos) + m_rsinbuf[inptr + nch] * (fracpos));
                        inptr++;
                    }
                    srcpos += drspos;
                    ret++;
                }
            }
        }

        if (m_filtercnt > 0)
        {
            if (m_ratio < 1.0 && ret > 0) // filter output
            {
                m_iirfilter ??= new WDLResampler_IIRFilter();
                int n = m_filtercnt;
                m_iirfilter.SetParms((float)m_ratio * m_filterpos, m_filterq);

                int x, a;
                int offs = 0;
                if (nch == 2)
                {
                    // Fast path for stereo
                    for (a = 0; a < n; a++)
                        m_iirfilter.Apply(m_rsinbuf, 0, nsamples_in, 2, offs++);
                }
                else
                {
                    for (x = 0; x < nch; x++)
                        for (a = 0; a < n; a++)
                            m_iirfilter.Apply(m_rsinbuf, x, nsamples_in, 1, offs++);
                }
            }
        }

        if (ret > 0 && rsinbuf_availtemp > m_samples_in_rsinbuf) // we had to pad!!
        {
            // check for the case where rsinbuf_availtemp>m_samples_in_rsinbuf, decrease ret down to actual valid samples
            double adj = (srcpos - m_samples_in_rsinbuf + outlatadj) / drspos;
            if (adj > 0)
            {
                ret -= (int)(adj + 0.5);
                if (ret < 0) ret = 0;
            }
        }

        int isrcpos = (int)srcpos;
        m_fracpos = srcpos - isrcpos;
        m_samples_in_rsinbuf -= isrcpos;
        if (m_samples_in_rsinbuf <= 0)
        {
            m_samples_in_rsinbuf = 0;
        }
        else
        {
            // TODO: bug here
            Array.Copy(m_rsinbuf, localin + isrcpos * nch, m_rsinbuf, localin, m_samples_in_rsinbuf * nch);
        }



        return ret;
    }

    // only called in sinc modes
    private void BuildLowPass(double filtpos)
    {
        int wantsize = m_sincsize;
        int wantinterp = m_sincoversize;

        if (m_filter_ratio != filtpos ||
            m_filter_coeffs_size != wantsize ||
            m_lp_oversize != wantinterp)
        {
            m_lp_oversize = wantinterp;
            m_filter_ratio = filtpos;

            // build lowpass filter
            int allocsize = (wantsize + 1) * m_lp_oversize;
            if (m_filter_coeffs.Length < allocsize)
                m_filter_coeffs = new float[allocsize];
            m_filter_coeffs_size = wantsize;

            int sz = wantsize * m_lp_oversize;
            int hsz = sz / 2;
            double filtpower = 0.0;
            double windowpos = 0.0;
            double dwindowpos = 2.0 * PI / (double)(sz);
            double dsincpos = PI / m_lp_oversize * filtpos; // filtpos is outrate/inrate, i.e. 0.5 is going to half rate
            double sincpos = dsincpos * (double)(-hsz);

            int x;
            for (x = -hsz; x < hsz + m_lp_oversize; x++)
            {
                double val = 0.35875 - 0.48829 * Math.Cos(windowpos) + 0.14128 * Math.Cos(2 * windowpos) - 0.01168 * Math.Cos(6 * windowpos); // blackman-harris
                if (x != 0) val *= Math.Sin(sincpos) / sincpos;

                windowpos += dwindowpos;
                sincpos += dsincpos;

                m_filter_coeffs[hsz + x] = (float)val;
                if (x < hsz) filtpower += val;
            }
            filtpower = m_lp_oversize / filtpower;
            for (x = 0; x < sz + m_lp_oversize; x++)
            {
                m_filter_coeffs[x] = (float)(m_filter_coeffs[x] * filtpower);
            }
        }
    }

    // TODO: Vectorised versions of the sinc methods
    // SincSample(float *outptr, float *inptr, double fracpos, int nch, float *filter, int filtsz)
    private void SincSample(float[] outBuffer, int outBufferIndex, float[] inBuffer, int inBufferIndex, double fracpos, int nch, float[] filter, int filterIndex, int filtsz)
    {
        int oversize = m_lp_oversize;
        fracpos *= oversize;
        int ifpos = (int)fracpos;
        filterIndex += oversize - 1 - ifpos;
        fracpos -= ifpos;

        for (int x = 0; x < nch; x++)
        {
            double sum = 0.0, sum2 = 0.0;
            int fptr = filterIndex;
            int iptr = inBufferIndex + x;
            int i = filtsz;
            while (i-- != 0)
            {
                sum += filter[fptr] * inBuffer[iptr];
                sum2 += filter[fptr + 1] * inBuffer[iptr];
                iptr += nch;
                fptr += oversize;
            }
            outBuffer[outBufferIndex + x] = (float)(sum * fracpos + sum2 * (1.0 - fracpos));
        }
    }

    // SincSample1(float* outptr, float* inptr, double fracpos, float* filter, int filtsz)
    private void SincSample1(float[] outBuffer, int outBufferIndex, float[] inBuffer, int inBufferIndex, double fracpos, float[] filter, int filterIndex, int filtsz)
    {
        int oversize = m_lp_oversize;
        fracpos *= oversize;
        int ifpos = (int)fracpos;
        filterIndex += oversize - 1 - ifpos;
        fracpos -= ifpos;

        double sum = 0.0, sum2 = 0.0;
        int fptr = filterIndex;
        int iptr = inBufferIndex;
        int i = filtsz;
        while (i-- != 0)
        {
            sum += filter[fptr] * inBuffer[iptr];
            sum2 += filter[fptr + 1] * inBuffer[iptr];
            iptr++;
            fptr += oversize;
        }
        outBuffer[outBufferIndex] = (float)(sum * fracpos + sum2 * (1.0 - fracpos));
    }

    // SincSample2(float* outptr, float* inptr, double fracpos, float* filter, int filtsz)
    private void SincSample2(float[] outptr, int outBufferIndex, float[] inBuffer, int inBufferIndex, double fracpos, float[] filter, int filterIndex, int filtsz)
    {
        int oversize = m_lp_oversize;
        fracpos *= oversize;
        int ifpos = (int)fracpos;
        filterIndex += oversize - 1 - ifpos;
        fracpos -= ifpos;

        double sum = 0.0;
        double sum2 = 0.0;
        double sumb = 0.0;
        double sum2b = 0.0;
        int fptr = filterIndex;
        int iptr = inBufferIndex;
        int i = filtsz / 2;
        while (i-- != 0)
        {
            sum += filter[fptr] * inBuffer[iptr];
            sum2 += filter[fptr] * inBuffer[iptr + 1];
            sumb += filter[fptr + 1] * inBuffer[iptr];
            sum2b += filter[fptr + 1] * inBuffer[iptr + 1];
            sum += filter[fptr + oversize] * inBuffer[iptr + 2];
            sum2 += filter[fptr + oversize] * inBuffer[iptr + 3];
            sumb += filter[fptr + oversize + 1] * inBuffer[iptr + 2];
            sum2b += filter[fptr + oversize + 1] * inBuffer[iptr + 3];
            iptr += 4;
            fptr += oversize * 2;
        }
        outptr[outBufferIndex + 0] = (float)(sum * fracpos + sumb * (1.0 - fracpos));
        outptr[outBufferIndex + 1] = (float)(sum2 * fracpos + sum2b * (1.0 - fracpos));
    }

    class WDLResampler_IIRFilter
    {
        private float fpos;
        private float a1, a2;
        private float b0, b1, b2;
        private readonly float[] hist;
        private const int HISTORY_SAMPLES = 4;

        public WDLResampler_IIRFilter()
        {
            fpos = -1;
            hist = new float[WDL_RESAMPLE_MAX_FILTERS * WDL_RESAMPLE_MAX_NCH * HISTORY_SAMPLES];
        }

        public void Reset()
        {
            hist.AsSpan().Clear();
        }

        public void SetParms(float fpos, float Q)
        {
            if (Math.Abs(fpos - this.fpos) < 0.000001f)
                return;
            this.fpos = fpos;

            float pos = fpos * MathF.PI;
            var (cpos, spos) = MathF.SinCos(pos);

            float alpha = spos / (2 * Q);

            float sc = 1 / (1 + alpha);
            b1 = (1 - cpos) * sc;
            b2 = b0 = b1 * 0.5f;
            a1 = -2 * cpos * sc;
            a2 = (1 - alpha) * sc;

        }

        public void Apply(float[] buffer, int offset, int count, int channels, int ind)
        {
            if (Vector128.IsHardwareAccelerated && Fma.IsSupported)
            {
                if (channels == 2)
                    EQSampleProvider.ApplyFilterVecStereo(hist, buffer, offset, count, ind, a1, a2, b0, b1, b2);
                else
                    EQSampleProvider.ApplyFilterVec(hist, buffer, offset, count, channels, ind, a1, a2, b0, b1, b2);
            }
            else
                EQSampleProvider.ApplyFilterScalar(hist, buffer, offset, count, channels, ind, a1, a2, b0, b1, b2);
        }
    }
}