/// <summary>
    /// Implements Packetization and Depacketization of packets defined in <see href="https://tools.ietf.org/html/rfc6184">RFC6184</see>.
    /// </summary>
    public class RFC6184Frame : Rtp.RtpFrame
    {
        /// <summary>
        /// Emulation Prevention
        /// </summary>
        static byte[] NalStart = { 0x00, 0x00, 0x01 };

        public RFC6184Frame(byte payloadType) : base(payloadType) { }

        public RFC6184Frame(Rtp.RtpFrame existing) : base(existing) { }

        public RFC6184Frame(RFC6184Frame f) : this((Rtp.RtpFrame)f) { Buffer = f.Buffer; }

        public System.IO.MemoryStream Buffer { get; set; }

        /// <summary>
        /// Creates any <see cref="Rtp.RtpPacket"/>'s required for the given nal
        /// </summary>
        /// <param name="nal">The nal</param>
        /// <param name="mtu">The mtu</param>
        public virtual void Packetize(byte[] nal, int mtu = 1500)
        {
            if (nal == null) return;

            int nalLength = nal.Length;

            int offset = 0;

            if (nalLength >= mtu)
            {
                //Make a Fragment Indicator with start bit
                byte[] FUI = new byte[] { (byte)(1 << 7), 0x00 };

                bool marker = false;

                while (offset < nalLength)
                {
                    //Set the end bit if no more data remains
                    if (offset + mtu > nalLength)
                    {
                        FUI[0] |= (byte)(1 << 6);
                        marker = true;
                    }
                    else if (offset > 0) //For packets other than the start
                    {
                        //No Start, No End
                        FUI[0] = 0;
                    }

                    //Add the packet
                    Add(new Rtp.RtpPacket(2, false, false, marker, PayloadTypeByte, 0, SynchronizationSourceIdentifier, HighestSequenceNumber + 1, 0, FUI.Concat(nal.Skip(offset).Take(mtu)).ToArray()));

                    //Move the offset
                    offset += mtu;
                }
            } //Should check for first byte to be 1 - 23?
            else Add(new Rtp.RtpPacket(2, false, false, true, PayloadTypeByte, 0, SynchronizationSourceIdentifier, HighestSequenceNumber + 1, 0, nal));
        }

        /// <summary>
        /// Creates <see cref="Buffer"/> with a H.264 RBSP from the contained packets
        /// </summary>
        public virtual void Depacketize() { bool sps, pps, sei, slice, idr; Depacketize(out sps, out pps, out sei, out slice, out idr); }

        /// <summary>
        /// Parses all contained packets and writes any contained Nal Units in the RBSP to <see cref="Buffer"/>.
        /// </summary>
        /// <param name="containsSps">Indicates if a Sequence Parameter Set was found</param>
        /// <param name="containsPps">Indicates if a Picture Parameter Set was found</param>
        /// <param name="containsSei">Indicates if Supplementatal Encoder Information was found</param>
        /// <param name="containsSlice">Indicates if a Slice was found</param>
        /// <param name="isIdr">Indicates if a IDR Slice was found</param>
        public virtual void Depacketize(out bool containsSps, out bool containsPps, out bool containsSei, out bool containsSlice, out bool isIdr)
        {
            containsSps = containsPps = containsSei = containsSlice = isIdr = false;

            DisposeBuffer();

            this.Buffer = new MemoryStream();

            //Get all packets in the frame
            foreach (Rtp.RtpPacket packet in m_Packets.Values.Distinct())
                ProcessPacket(packet, out containsSps, out containsPps, out containsSei, out containsSlice, out isIdr);

            //Order by DON?
            this.Buffer.Position = 0;
        }

        /// <summary>
        /// Depacketizes a single packet.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="containsSps"></param>
        /// <param name="containsPps"></param>
        /// <param name="containsSei"></param>
        /// <param name="containsSlice"></param>
        /// <param name="isIdr"></param>
        internal protected virtual void ProcessPacket(Rtp.RtpPacket packet, out bool containsSps, out bool containsPps, out bool containsSei, out bool containsSlice, out bool isIdr)
        {
            containsSps = containsPps = containsSei = containsSlice = isIdr = false;

            //Starting at offset 0
            int offset = 0;

            //Obtain the data of the packet (without source list or padding)
            byte[] packetData = packet.Coefficients.ToArray();

            //Cache the length
            int count = packetData.Length;

            //Must have at least 2 bytes
            if (count <= 2) return;

            //Determine if the forbidden bit is set and the type of nal from the first byte
            byte firstByte = packetData[offset];

            //bool forbiddenZeroBit = ((firstByte & 0x80) >> 7) != 0;

            byte nalUnitType = (byte)(firstByte & Common.Binary.FiveBitMaxValue);

            //o  The F bit MUST be cleared if all F bits of the aggregated NAL units are zero; otherwise, it MUST be set.
            //if (forbiddenZeroBit && nalUnitType <= 23 && nalUnitType > 29) throw new InvalidOperationException("Forbidden Zero Bit is Set.");

            //Determine what to do
            switch (nalUnitType)
            {
                //Reserved - Ignore
                case 0:
                case 30:
                case 31:
                    {
                        return;
                    }
                case 24: //STAP - A
                case 25: //STAP - B
                case 26: //MTAP - 16
                case 27: //MTAP - 24
                    {
                        //Move to Nal Data
                        ++offset;

                        //Todo Determine if need to Order by DON first.
                        //EAT DON for ALL BUT STAP - A
                        if (nalUnitType != 24) offset += 2;

                        //Consume the rest of the data from the packet
                        while (offset < count)
                        {
                            //Determine the nal unit size which does not include the nal header
                            int tmp_nal_size = Common.Binary.Read16(packetData, offset, BitConverter.IsLittleEndian);
                            offset += 2;

                            //If the nal had data then write it
                            if (tmp_nal_size > 0)
                            {
                                //For DOND and TSOFFSET
                                switch (nalUnitType)
                                {
                                    case 25:// MTAP - 16
                                        {
                                            //SKIP DOND and TSOFFSET
                                            offset += 3;
                                            goto default;
                                        }
                                    case 26:// MTAP - 24
                                        {
                                            //SKIP DOND and TSOFFSET
                                            offset += 4;
                                            goto default;
                                        }
                                    default:
                                        {
                                            //Read the nal header but don't move the offset
                                            byte nalHeader = (byte)(packetData[offset] & Common.Binary.FiveBitMaxValue);

                                            if (nalHeader > 5)
                                            {
                                                if (nalHeader == 6)
                                                {
                                                    Buffer.WriteByte(0);
                                                    containsSei = true;
                                                }
                                                else if (nalHeader == 7)
                                                {
                                                    Buffer.WriteByte(0);
                                                    containsPps = true;
                                                }
                                                else if (nalHeader == 8)
                                                {
                                                    Buffer.WriteByte(0);
                                                    containsSps = true;
                                                }
                                            }

                                            if (nalHeader == 1) containsSlice = true;

                                            if (nalHeader == 5) isIdr = true;

                                            //Done reading
                                            break;
                                        }
                                }

                                //Write the start code
                                Buffer.Write(NalStart, 0, 3);

                                //Write the nal header and data
                                Buffer.Write(packetData, offset, tmp_nal_size);

                                //Move the offset past the nal
                                offset += tmp_nal_size;
                            }
                        }

                        return;
                    }
                case 28: //FU - A
                case 29: //FU - B
                    {
                        /*
                         Informative note: When an FU-A occurs in interleaved mode, it
                         always follows an FU-B, which sets its DON.
                         * Informative note: If a transmitter wants to encapsulate a single
                          NAL unit per packet and transmit packets out of their decoding
                          order, STAP-B packet type can be used.
                         */
                        //Need 2 bytes
                        if (count > 2)
                        {
                            //Read the Header
                            byte FUHeader = packetData[++offset];

                            bool Start = ((FUHeader & 0x80) >> 7) > 0;

                            //bool End = ((FUHeader & 0x40) >> 6) > 0;

                            //bool Receiver = (FUHeader & 0x20) != 0;

                            //if (Receiver) throw new InvalidOperationException("Receiver Bit Set");

                            //Move to data
                            ++offset;

                            //Todo Determine if need to Order by DON first.
                            //DON Present in FU - B
                            if (nalUnitType == 29) offset += 2;

                            //Determine the fragment size
                            int fragment_size = count - offset;

                            //If the size was valid
                            if (fragment_size > 0)
                            {
                                //If the start bit was set
                                if (Start)
                                {
                                    //Reconstruct the nal header
                                    //Use the first 3 bits of the first byte and last 5 bites of the FU Header
                                    byte nalHeader = (byte)((firstByte & 0xE0) | (FUHeader & Common.Binary.FiveBitMaxValue));

                                    //Could have been SPS / PPS / SEI
                                    if (nalHeader > 5)
                                    {
                                        if (nalHeader == 6)
                                        {
                                            Buffer.WriteByte(0);
                                            containsSei = true;
                                        }
                                        else if (nalHeader == 7)
                                        {
                                            Buffer.WriteByte(0);
                                            containsPps = true;
                                        }
                                        else if (nalHeader == 8)
                                        {
                                            Buffer.WriteByte(0);
                                            containsSps = true;
                                        }
                                    }

                                    if (nalHeader == 1) containsSlice = true;

                                    if (nalHeader == 5) isIdr = true;

                                    //Write the start code
                                    Buffer.Write(NalStart, 0, 3);

                                    //Write the re-construced header
                                    Buffer.WriteByte(nalHeader);
                                }

                                //Write the data of the fragment.
                                Buffer.Write(packetData, offset, fragment_size);
                            }
                        }
                        return;
                    }
                default:
                    {
                        // 6 SEI, 7 and 8 are SPS and PPS
                        if (nalUnitType > 5)
                        {
                            if (nalUnitType == 6)
                            {
                                Buffer.WriteByte(0);
                                containsSei = true;
                            }
                            else if (nalUnitType == 7)
                            {
                                Buffer.WriteByte(0);
                                containsPps = true;
                            }
                            else if (nalUnitType == 8)
                            {
                                Buffer.WriteByte(0);
                                containsSps = true;
                            }
                        }

                        if (nalUnitType == 1) containsSlice = true;

                        if (nalUnitType == 5) isIdr = true;

                        //Write the start code
                        Buffer.Write(NalStart, 0, 3);

                        //Write the nal heaer and data data
                        Buffer.Write(packetData, offset, count - offset);

                        return;
                    }
            }
        }

        internal void DisposeBuffer()
        {
            if (Buffer != null)
            {
                Buffer.Dispose();
                Buffer = null;
            }
        }

        public override void Dispose()
        {
            if (Disposed) return;
            base.Dispose();
            DisposeBuffer();
        }

        //To go to an Image...
        //Look for a SliceHeader in the Buffer
        //Decode Macroblocks in Slice
        //Convert Yuv to Rgb
    }